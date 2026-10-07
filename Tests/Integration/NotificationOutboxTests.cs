using System.Text.Json;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Models.Messaging;
using DAL.Context;
using DAL.Enum;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebAPI.Services;
using Xunit;
using OutboxMessage = DAL.Entities.NotificationOutboxMessage;
using OutboxStatus = DAL.Entities.NotificationOutboxStatus;
using TaskEntity = DAL.Entities.Task;

namespace Tests.Integration;

public class NotificationOutboxTests
{
    [Fact]
    public async Task RepeatedScan_PublishesEachOccurrenceOnce_AndMarksOnlyAfterConfirmation()
    {
        await using var h = await Harness.CreateAsync();
        await h.RunAsync();
        await h.RunAsync();
        await using var db = h.CreateContext();
        Assert.Single(h.Queue.Attempts);
        var message = await db.NotificationOutbox.SingleAsync();
        Assert.Equal(OutboxStatus.Published, message.Status);
        Assert.NotNull((await db.Tasks.FindAsync(h.TaskId))!.ReminderSentAt);
        Assert.Equal(message.Id.ToString("N"), JsonSerializer.Deserialize<TaskNotificationEnvelope>(message.Payload)!.CorrelationId);
    }

    [Fact]
    public async Task FailedPublish_RemainsPending_AcrossSchedulerRestart()
    {
        await using var h = await Harness.CreateAsync();
        h.Queue.Accept = false;
        await h.RunAsync();
        await using (var db = h.CreateContext())
        {
            Assert.Equal(OutboxStatus.Pending, (await db.NotificationOutbox.SingleAsync()).Status);
            Assert.Null((await db.Tasks.FindAsync(h.TaskId))!.ReminderSentAt);
        }
        h.Queue.Accept = true;
        h.Clock.Advance(TimeSpan.FromMinutes(2));
        await h.RunAsync(); // RunAsync constructs a new scheduler and fresh scopes.
        Assert.Equal(2, h.Queue.Attempts.Count);
        Assert.Equal(h.Queue.Attempts[0], h.Queue.Attempts[1]);
        await using var finalDb = h.CreateContext();
        Assert.Equal(OutboxStatus.Published, (await finalDb.NotificationOutbox.SingleAsync()).Status);
    }

    [Fact]
    public async Task SaveFailureAfterPublication_RetriesSameEnvelopeAfterLeaseExpiry()
    {
        await using var h = await Harness.CreateAsync();
        h.Failures.FailPublishedSave = true;
        await h.RunAsync();
        await h.RunAsync(); // Active lease prevents an immediate duplicate send.
        Assert.Single(h.Queue.Attempts);
        await using (var db = h.CreateContext())
        {
            Assert.Equal(OutboxStatus.Pending, (await db.NotificationOutbox.SingleAsync()).Status);
            Assert.Null((await db.Tasks.FindAsync(h.TaskId))!.ReminderSentAt);
        }
        h.Clock.Advance(TimeSpan.FromMinutes(6));
        await h.RunAsync();
        Assert.Equal(2, h.Queue.Attempts.Count);
        Assert.Equal(h.Queue.Attempts[0], h.Queue.Attempts[1]);
        await using var finalDb = h.CreateContext();
        Assert.Equal(OutboxStatus.Published, (await finalDb.NotificationOutbox.SingleAsync()).Status);
    }

    [Fact]
    public async Task EnqueueSaveFailure_DoesNotPublishOrLoseTheOccurrence()
    {
        await using var h = await Harness.CreateAsync();
        h.Failures.FailEnqueueSave = true;
        await Assert.ThrowsAsync<IOException>(() => h.RunAsync());
        Assert.Empty(h.Queue.Attempts);
        await using (var db = h.CreateContext())
        {
            Assert.Empty(await db.NotificationOutbox.ToListAsync());
            Assert.Null((await db.Tasks.FindAsync(h.TaskId))!.ReminderNotificationId);
        }
        await h.RunAsync();
        Assert.Single(h.Queue.Attempts);
    }

    [Fact]
    public async Task CompetingScans_RollBackTheLosingOutboxInsert()
    {
        await using var h = await Harness.CreateAsync();
        await using var first = h.CreateContext();
        await using var second = h.CreateContext();
        var task1 = (await first.Tasks.FindAsync(h.TaskId))!;
        var task2 = (await second.Tasks.FindAsync(h.TaskId))!;
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        task1.ReminderNotificationId = id1;
        task2.ReminderNotificationId = id2;
        first.NotificationOutbox.Add(NewMessage(id1, h.TaskId));
        second.NotificationOutbox.Add(NewMessage(id2, h.TaskId));
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var check = h.CreateContext();
        Assert.Equal(id1, (await check.NotificationOutbox.SingleAsync()).Id);
        Assert.Equal(id1, (await check.Tasks.FindAsync(h.TaskId))!.ReminderNotificationId);
    }

    [Fact]
    public async Task TwoWorkers_DoNotDispatchTheSameActiveClaim()
    {
        await using var h = await Harness.CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Queue.BeforeReturn = async () => { started.SetResult(); await release.Task; };
        var firstWorker = h.RunAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await h.RunAsync();
            Assert.Single(h.Queue.Attempts);
        }
        finally { release.TrySetResult(); }
        await firstWorker;
    }

    [Fact]
    public async Task DisabledReminder_CancelsPendingEnvelope()
    {
        await using var h = await Harness.CreateAsync();
        h.Queue.Accept = false;
        await h.RunAsync();
        await using (var db = h.CreateContext())
        {
            var task = (await db.Tasks.FindAsync(h.TaskId))!;
            task.ReminderEnabled = false;
            task.ReminderNotificationId = null;
            await db.SaveChangesAsync();
        }
        h.Clock.Advance(TimeSpan.FromMinutes(2));
        h.Queue.Accept = true;
        await h.RunAsync();
        Assert.Single(h.Queue.Attempts);
        await using var finalDb = h.CreateContext();
        Assert.Equal(OutboxStatus.Canceled, (await finalDb.NotificationOutbox.SingleAsync()).Status);
    }

    [Fact]
    public void SqlServerMigrationSnapshot_MatchesTheRuntimeModel()
    {
        using var db = new TaskContext(new DbContextOptionsBuilder<TaskContext>()
            .UseSqlServer("Server=localhost;Database=metadata-only;Integrated Security=true").Options);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static OutboxMessage NewMessage(Guid id, int taskId) => new()
    {
        Id = id, TaskId = taskId, QueueName = "TaskReminders", Payload = "{}", CreatedAtUtc = DateTime.UtcNow
    };

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }

    private sealed class TestQueue : IQueueService
    {
        public bool Accept { get; set; } = true;
        public List<string> Attempts { get; } = new();
        public Func<Task>? BeforeReturn { get; set; }
        public async Task<bool> PostValue(string message, string? queueName = null, CancellationToken ct = default)
        {
            Attempts.Add(message);
            if (BeforeReturn is not null) await BeforeReturn();
            return Accept;
        }
    }

    private sealed class SaveFailures : SaveChangesInterceptor
    {
        public bool FailEnqueueSave { get; set; }
        public bool FailPublishedSave { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            var entries = data.Context!.ChangeTracker.Entries<OutboxMessage>().ToList();
            if (FailEnqueueSave && entries.Any(e => e.State == EntityState.Added))
            {
                FailEnqueueSave = false;
                throw new IOException("Simulated enqueue commit failure");
            }
            if (FailPublishedSave && entries.Any(e => e.State == EntityState.Modified && e.Entity.Status == OutboxStatus.Published))
            {
                FailPublishedSave = false;
                throw new IOException("Simulated failure after broker confirmation");
            }
            return base.SavingChangesAsync(data, result, ct);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private ServiceProvider _provider = null!;
        public readonly TestClock Clock = new();
        public readonly TestQueue Queue = new();
        public readonly SaveFailures Failures = new();
        public int TaskId { get; private set; }
        public TaskContext CreateContext() => new(new DbContextOptionsBuilder<TaskContext>()
            .UseSqlite(_connection).AddInterceptors(Failures).Options);

        public static async Task<Harness> CreateAsync()
        {
            var h = new Harness();
            await h._connection.OpenAsync();
            await using (var db = h.CreateContext())
            {
                await db.Database.EnsureCreatedAsync();
                var task = new TaskEntity
                {
                    Name = "Outbox regression", Description = "Test task", ProjectId = 1, PerformerId = 1,
                    State = TaskState.ToDo, CreatedAt = h.Clock.GetUtcNow().UtcDateTime,
                    DueDate = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(5),
                    ReminderEnabled = true, ReminderOffsetMinutes = 15
                };
                db.Tasks.Add(task);
                await db.SaveChangesAsync();
                h.TaskId = task.Id;
            }
            var services = new ServiceCollection();
            services.AddScoped(_ => h.CreateContext());
            services.AddSingleton<IQueueService>(h.Queue);
            h._provider = services.BuildServiceProvider();
            return h;
        }

        public Task RunAsync() => new TaskReminderScheduler(
            _provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new ReminderOptions()),
            Options.Create(new RabbitMqOptions { ReminderQueueName = "TaskReminders" }),
            NullLogger<TaskReminderScheduler>.Instance, Clock).RunOnceAsync();

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
