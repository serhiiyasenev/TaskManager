using System.Text.Json;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Models.Messaging;
using BLL.Services.Notifications;
using DAL.Context;
using NotificationOutboxKind = DAL.Entities.NotificationOutboxKind;
using NotificationOutboxStatus = DAL.Entities.NotificationOutboxStatus;
using NotificationOutboxMessage = DAL.Entities.NotificationOutboxMessage;
using Team = DAL.Entities.Team;
using Project = DAL.Entities.Project;
using User = DAL.Entities.User;
using DAL.Enum;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using TaskEntity = DAL.Entities.Task;

namespace Tests.Integration;

public sealed class TaskNotificationWorkerTests
{
    [Theory]
    [InlineData(NotificationOutboxKind.TaskCreated)]
    [InlineData(NotificationOutboxKind.ExecutedTaskCompleted)]
    public async Task DisabledRemindersStillDispatchCreatedAndCompletedWithoutReminderOccurrence(NotificationOutboxKind kind)
    {
        await using var h = await Harness.CreateAsync();
        var id = await h.AddAsync(kind);
        var scheduler = new TaskNotificationScheduler(h.Scopes, Options.Create(new ReminderOptions { Enabled = false }),
            Options.Create(new RabbitMqOptions { QueueName = "TaskNotifications", ReminderQueueName = "TaskReminders" }),
            NullLogger<TaskNotificationScheduler>.Instance, h.Clock);
        await scheduler.RunOnceAsync();
        var sent = Assert.Single(h.Queue.Bodies);
        Assert.Equal(id.ToString("N"), JsonSerializer.Deserialize<TaskNotificationEnvelope>(sent)!.CorrelationId);
        await using var db = h.Context();
        Assert.Equal(NotificationOutboxStatus.Published, (await db.NotificationOutbox.SingleAsync()).Status);
        var task = await db.Tasks.SingleAsync();
        Assert.Null(task.ReminderNotificationId);
        Assert.Null(task.ReminderSentAt);
        Assert.Null(task.EscalationNotificationId);
        Assert.Null(task.EscalationSentAt);
    }

    [Fact]
    public async Task WorkerHonorsConfiguredReminderQueueAndGeneratesStableOccurrence()
    {
        await using var h = await Harness.CreateAsync();
        var scheduler = new TaskNotificationScheduler(h.Scopes, Options.Create(new ReminderOptions()),
            Options.Create(new RabbitMqOptions { ReminderQueueName = "TaskReminders" }),
            NullLogger<TaskNotificationScheduler>.Instance, h.Clock);
        await scheduler.RunOnceAsync();
        await scheduler.RunOnceAsync();
        Assert.Single(h.Queue.Bodies);
        Assert.Equal("TaskReminders", Assert.Single(h.Queue.Queues));
        await using var db = h.Context();
        var outbox = await db.NotificationOutbox.SingleAsync();
        Assert.Equal(NotificationOutboxKind.Reminder, outbox.Kind);
        Assert.Equal(outbox.Id, (await db.Tasks.SingleAsync()).ReminderNotificationId);
        Assert.Equal(outbox.Id.ToString("N"), JsonSerializer.Deserialize<TaskNotificationEnvelope>(outbox.Payload)!.CorrelationId);
    }

    [Fact]
    public async Task DispatcherFalsePublishKeepsCreatedIntentPendingAndRetriesSameImmutableBody()
    {
        await using var h = await Harness.CreateAsync();
        var id = await h.AddAsync(NotificationOutboxKind.TaskCreated);
        h.Queue.Accept = false;
        Assert.False(await h.Dispatcher.TryDispatchAsync(id));
        await using (var db = h.Context())
        {
            var task = await db.Tasks.SingleAsync(); task.Name = "Renamed after first send attempt";
            await db.SaveChangesAsync();
            Assert.Equal(NotificationOutboxStatus.Pending, (await db.NotificationOutbox.SingleAsync()).Status);
        }
        h.Queue.Accept = true; h.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await h.Dispatcher.TryDispatchAsync(id));
        Assert.Equal(2, h.Queue.Bodies.Count);
        Assert.Equal(h.Queue.Bodies[0], h.Queue.Bodies[1]);
        Assert.Equal(id.ToString("N"), JsonSerializer.Deserialize<TaskNotificationEnvelope>(h.Queue.Bodies[1])!.CorrelationId);
    }

    [Fact]
    public async Task ReassignedTaskSuppressesOldCreatedIntentWithoutSendingToOldOrNewRecipient()
    {
        await using var h = await Harness.CreateAsync();
        var id = await h.AddAsync(NotificationOutboxKind.TaskCreated);
        await using (var db = h.Context())
        { (await db.Tasks.SingleAsync()).PerformerId = 2; await db.SaveChangesAsync(); }
        Assert.False(await h.Dispatcher.TryDispatchAsync(id));
        Assert.Empty(h.Queue.Bodies);
        await using var final = h.Context();
        Assert.Equal(NotificationOutboxStatus.Canceled, (await final.NotificationOutbox.SingleAsync()).Status);
    }

    [Fact]
    public async Task DepartedMemberCannotReceivePendingCreatedIntent()
    {
        await using var h = await Harness.CreateAsync();
        var id = await h.AddAsync(NotificationOutboxKind.TaskCreated);
        await using (var db = h.Context())
        { (await db.Users.SingleAsync(user => user.Id == 1)).TeamId = null; await db.SaveChangesAsync(); }
        Assert.False(await h.Dispatcher.TryDispatchAsync(id));
        Assert.Empty(h.Queue.Bodies);
    }

    [Fact]
    public async Task MalformedCanonicalOutboxIdentityCannotBePublished()
    {
        await using var h = await Harness.CreateAsync();
        var id = await h.AddAsync(NotificationOutboxKind.TaskCreated);
        await using (var db = h.Context())
        { (await db.NotificationOutbox.SingleAsync()).Payload = "{}"; await db.SaveChangesAsync(); }
        Assert.False(await h.Dispatcher.TryDispatchAsync(id));
        Assert.Empty(h.Queue.Bodies);
    }

    [Fact]
    public async Task ImmediateRequestAndWorkerCannotUseTheSameActiveClaimTwice()
    {
        await using var h = await Harness.CreateAsync();
        var id = await h.AddAsync(NotificationOutboxKind.TaskCreated);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Queue.BeforeReturn = async () => { entered.SetResult(); await release.Task; };
        var first = h.Dispatcher.TryDispatchAsync(id);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(await h.Dispatcher.TryDispatchAsync(id));
            Assert.Single(h.Queue.Bodies);
        }
        finally { release.TrySetResult(); }
        Assert.True(await first);
    }

    [Fact]
    public async Task StalePreClaimReaderCannotBypassWinningFailedPublishBackoff()
    {
        await using var h = await Harness.CreateAsync();
        var id = await h.AddAsync(NotificationOutboxKind.TaskCreated);
        h.Queue.Accept = false;
        h.Gate.Enabled = true;
        var stale = h.Dispatcher.TryDispatchAsync(id);
        DateTime? winningDeadline = null;
        try
        {
            await h.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(await h.Dispatcher.TryDispatchAsync(id));
            await using var afterWinner = h.Context();
            var winning = await afterWinner.NotificationOutbox.SingleAsync();
            Assert.Equal(NotificationOutboxStatus.Pending, winning.Status);
            Assert.Null(winning.LockId);
            winningDeadline = winning.LockedUntilUtc;
            Assert.True(winningDeadline > h.Clock.GetUtcNow().UtcDateTime);
        }
        finally { h.Gate.Release.TrySetResult(); }
        Assert.False(await stale);
        Assert.Single(h.Queue.Bodies);
        await using var final = h.Context();
        var message = await final.NotificationOutbox.SingleAsync();
        Assert.Equal(winningDeadline, message.LockedUntilUtc);
        Assert.Null(message.LockId);
        Assert.Equal(NotificationOutboxStatus.Pending, message.Status);
    }

    private sealed class ClaimPause : SaveChangesInterceptor
    {
        private int _paused;
        public bool Enabled { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Enabled && data.Context!.ChangeTracker.Entries<NotificationOutboxMessage>().Any(entry =>
                    entry.State == EntityState.Modified && entry.Entity.Status == NotificationOutboxStatus.Pending && entry.Entity.LockId.HasValue)
                && Interlocked.CompareExchange(ref _paused, 1, 0) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return result;
        }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delay) => _now += delay;
    }
    private sealed class Queue : IQueueService
    {
        public bool Accept { get; set; } = true;
        public Func<Task>? BeforeReturn { get; set; }
        public List<string> Bodies { get; } = [];
        public List<string?> Queues { get; } = [];
        public async Task<bool> PostValue(string message, string? queueName = null, CancellationToken ct = default)
        { Bodies.Add(message); Queues.Add(queueName); if (BeforeReturn is not null) await BeforeReturn(); return Accept; }
    }
    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private ServiceProvider _provider = null!;
        public Clock Clock { get; } = new();
        public Queue Queue { get; } = new();
        public ClaimPause Gate { get; } = new();
        public IServiceScopeFactory Scopes => _provider.GetRequiredService<IServiceScopeFactory>();
        public TaskNotificationDispatcher Dispatcher => new(Scopes, NullLogger<TaskNotificationDispatcher>.Instance, Clock);
        public TaskContext Context() => new(new DbContextOptionsBuilder<TaskContext>().UseSqlite(_connection).AddInterceptors(Gate).Options);
        public static async Task<Harness> CreateAsync()
        {
            var h = new Harness(); await h._connection.OpenAsync();
            await using var db = h.Context(); await db.Database.EnsureCreatedAsync();
            db.Teams.Add(new Team { Id = 1, Name = "Fixture team", CreatedAt = h.Clock.GetUtcNow().UtcDateTime });
            db.Users.AddRange(new User("one", "One", "Member", "one@example.test") { Id = 1, TeamId = 1, EmailConfirmed = true },
                new User("two", "Two", "Member", "two@example.test") { Id = 2, TeamId = 1, EmailConfirmed = true });
            db.Projects.Add(new Project { Id = 1, AuthorId = 1, TeamId = 1, Name = "Fixture project", Description = "Synthetic", CreatedAt = h.Clock.GetUtcNow().UtcDateTime });
            db.Tasks.Add(new TaskEntity { Id = 1, Name = "Immutable task name", Description = "Synthetic", ProjectId = 1, PerformerId = 1,
                State = TaskState.ToDo, CreatedAt = h.Clock.GetUtcNow().UtcDateTime, DueDate = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(5),
                ReminderEnabled = true, ReminderOffsetMinutes = 15 });
            await db.SaveChangesAsync();
            var services = new ServiceCollection().AddScoped(_ => h.Context()).AddSingleton<IQueueService>(h.Queue)
                .AddScoped<ITaskNotificationDispatcher>(_ => h.Dispatcher);
            h._provider = services.BuildServiceProvider(); return h;
        }
        public async Task<Guid> AddAsync(NotificationOutboxKind kind)
        {
            var id = Guid.NewGuid(); await using var db = Context();
            object envelope = kind == NotificationOutboxKind.TaskCreated
                ? new TaskNotificationEnvelope { Type = NotificationEventType.TaskCreated, CorrelationId = id.ToString("N"),
                    TaskCreated = new(id, 1, "Immutable task name", 1, 1, "Fixture project", Clock.GetUtcNow().UtcDateTime) }
                : new { Type = NotificationEventType.ExecutedTaskCompleted, CorrelationId = id.ToString("N"),
                    ExecutedTask = new { TaskId = 1, TaskName = "Immutable task name", CompletedAtUtc = Clock.GetUtcNow().UtcDateTime,
                        PerformerId = 1, ProjectId = 1, ProjectName = "Fixture project" } };
            db.NotificationOutbox.Add(new NotificationOutboxMessage { Id = id, TaskId = 1, Kind = kind, QueueName = "TaskNotifications",
                CreatedAtUtc = Clock.GetUtcNow().UtcDateTime, Payload = JsonSerializer.Serialize(envelope) });
            await db.SaveChangesAsync(); return id;
        }
        public async ValueTask DisposeAsync() { await _provider.DisposeAsync(); await _connection.DisposeAsync(); }
    }
}
