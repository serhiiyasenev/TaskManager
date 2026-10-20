using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Models.Messaging;
using BLL.Services.Notifications;
using DAL.Entities;
using DAL.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Integration;

// HTTP state commands run against the relational factory. Queue confirmation is
// controlled here; actual SQL lock/broker behavior has separate native tests.
public sealed class ThoroughTaskStateTests
{
    [Fact]
    public async Task SameLiveState_RequiresCurrentVersionAndDoesNotRewriteOccurrences()
    {
        using var h = await Harness.CreateAsync();
        await h.AddOldOccurrencesAsync(leased: true);
        using var caller = h.Factory.CreateUserClient(101);
        var before = await h.ReadAsync();

        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(caller, "InProgress", before.Version)).StatusCode);
        var winner = await h.ReadAsync();
        var outbox = await h.OutboxSnapshotAsync();
        Assert.NotEqual(before.Version, winner.Version);

        // A stale no-op must not bypass optimistic concurrency.
        Assert.Equal(HttpStatusCode.Conflict, (await ChangeAsync(caller, "InProgress", before.Version)).StatusCode);
        Assert.Equal(winner, await h.ReadAsync());
        Assert.Equal(outbox, await h.OutboxSnapshotAsync());

        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(caller, "InProgress", winner.Version)).StatusCode);
        Assert.Equal(winner, await h.ReadAsync());
        Assert.Equal(outbox, await h.OutboxSnapshotAsync());
        Assert.Empty(h.Queue.Bodies);
    }

    [Fact]
    public async Task CloseThenReopen_CannotReviveLeasedOldOccurrencesOrReuseTheirIds()
    {
        using var h = await Harness.CreateAsync();
        var old = await h.AddOldOccurrencesAsync(leased: true);
        using var caller = h.Factory.CreateUserClient(101);
        var before = await h.ReadAsync();

        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(caller, "Canceled", before.Version)).StatusCode);
        var closed = await h.ReadAsync();
        Assert.NotNull(closed.FinishedAt);
        Assert.Null(closed.ReminderNotificationId);
        Assert.Null(closed.EscalationNotificationId);
        await h.RunSchedulerAsync();
        Assert.Empty(h.Queue.Bodies);

        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(caller, "ToDo", closed.Version)).StatusCode);
        var reopened = await h.ReadAsync();
        Assert.Null(reopened.FinishedAt);
        await h.RunSchedulerAsync();
        var scheduled = await h.ReadAsync();
        Assert.NotEqual(reopened.Version, scheduled.Version);
        Assert.NotNull(scheduled.ReminderNotificationId);
        Assert.NotNull(scheduled.EscalationNotificationId);
        Assert.DoesNotContain(scheduled.ReminderNotificationId!.Value, old.Select(value => value.Id));
        Assert.DoesNotContain(scheduled.EscalationNotificationId!.Value, old.Select(value => value.Id));
        Assert.NotNull(scheduled.ReminderSentAt);
        Assert.NotNull(scheduled.EscalationSentAt);
        Assert.Equal(2, h.Queue.Bodies.Count);
        Assert.All(h.Queue.Bodies, body => Assert.DoesNotContain(
            Guid.Parse(JsonSerializer.Deserialize<TaskNotificationEnvelope>(body)!.CorrelationId!), old.Select(value => value.Id)));

        // Scheduler occurrence writes participate in the same version contract.
        Assert.Equal(HttpStatusCode.Conflict, (await ChangeAsync(caller, "Done", reopened.Version)).StatusCode);
        Assert.Equal(scheduled, await h.ReadAsync());
        h.Clock.Advance(TimeSpan.FromMinutes(6));
        await h.RunSchedulerAsync();
        await h.RunSchedulerAsync();
        Assert.Equal(2, h.Queue.Bodies.Count);
        await h.Factory.WithDatabaseAsync(async db =>
        {
            var oldIds = old.Select(value => value.Id).ToArray();
            var retired = await db.NotificationOutbox.Where(value => oldIds.Contains(value.Id)).ToListAsync();
            Assert.Equal(2, retired.Count);
            Assert.All(retired, value => { Assert.Equal(NotificationOutboxStatus.Canceled, value.Status); Assert.Null(value.PublishedAtUtc); });
        });
        // A delivery already in flight before closure is also durably suppressed
        // after reopening; it cannot create an inbox row for the replacement ID.
        foreach (var value in old) await h.AssertSuppressedAsync(value);
        await h.AssertOnlySuppressedMarkersAsync(2);
    }

    [Fact]
    public async Task BrokerConfirmsAfterTerminalWrite_CannotRestoreOldOccurrenceOrSentTimestamp()
    {
        using var h = await Harness.CreateAsync();
        var old = await h.AddOldOccurrencesAsync(leased: false);
        using var caller = h.Factory.CreateUserClient(101);
        var before = await h.ReadAsync();
        var stateChangedDuringSend = false;
        h.Queue.BeforeReturn = async () =>
        {
            h.Queue.BeforeReturn = null;
            var response = await ChangeAsync(caller, "Done", before.Version);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            stateChangedDuringSend = true;
        };

        using (var scope = h.Factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<ITaskNotificationDispatcher>().TryDispatchAsync(old[0].Id));
        Assert.True(stateChangedDuringSend);
        Assert.Single(h.Queue.Bodies);
        var closed = await h.ReadAsync();
        Assert.Equal(TaskState.Done, closed.State);
        Assert.Null(closed.ReminderNotificationId);
        Assert.Null(closed.EscalationNotificationId);
        Assert.Null(closed.ReminderSentAt);
        Assert.Null(closed.EscalationSentAt);
        Assert.NotEqual(before.Version, closed.Version);
        await h.AssertSuppressedAsync(old[0]);

        h.Clock.Advance(TimeSpan.FromMinutes(6));
        await h.RunSchedulerAsync();
        Assert.Single(h.Queue.Bodies);
        Assert.Equal(closed, await h.ReadAsync());
        await h.Factory.WithDatabaseAsync(async db => Assert.All(await db.NotificationOutbox.ToListAsync(), value =>
        {
            Assert.Equal(NotificationOutboxStatus.Canceled, value.Status);
            Assert.Null(value.PublishedAtUtc);
            Assert.Null(value.LockId);
            Assert.Null(value.LockedUntilUtc);
        }));
        await h.AssertOnlySuppressedMarkersAsync(1);
    }

    [Fact]
    public async Task ReopenAfterConfirmedReminders_DoesNotSendTheSameDeadlineAgain()
    {
        using var h = await Harness.CreateAsync();
        using var caller = h.Factory.CreateUserClient(101);
        await h.RunSchedulerAsync();
        var sent = await h.ReadAsync();
        Assert.Equal(2, h.Queue.Bodies.Count);
        Assert.NotNull(sent.ReminderSentAt);
        Assert.NotNull(sent.EscalationSentAt);
        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(caller, "Done", sent.Version)).StatusCode);
        var closed = await h.ReadAsync();
        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(caller, "InProgress", closed.Version)).StatusCode);
        var reopened = await h.ReadAsync();
        Assert.Equal(sent.ReminderSentAt, reopened.ReminderSentAt);
        Assert.Equal(sent.EscalationSentAt, reopened.EscalationSentAt);
        Assert.Null(reopened.ReminderNotificationId);
        Assert.Null(reopened.EscalationNotificationId);

        h.Clock.Advance(TimeSpan.FromMinutes(6));
        await h.RunSchedulerAsync();
        await h.RunSchedulerAsync();
        Assert.Equal(2, h.Queue.Bodies.Count);
        Assert.Equal(reopened, await h.ReadAsync());
        await h.Factory.WithDatabaseAsync(async db =>
        {
            var rows = await db.NotificationOutbox.ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.Equal(NotificationOutboxStatus.Published, row.Status));
        });
    }

    private static Task<HttpResponseMessage> ChangeAsync(HttpClient client, string state, Guid version) =>
        client.PatchAsJsonAsync("/api/tasks/101/state", new { state, expectedVersion = version });

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class Queue : IQueueService
    {
        public List<string> Bodies { get; } = [];
        public Func<Task>? BeforeReturn { get; set; }
        public async Task<bool> PostValue(string message, string? queueName = null, CancellationToken ct = default)
        {
            Bodies.Add(message);
            if (BeforeReturn is not null) await BeforeReturn();
            return true;
        }
    }

    private sealed record OldOccurrence(Guid Id, string Payload);
    private sealed record TaskSnapshot(TaskState State, DateTime? FinishedAt, Guid Version,
        DateTime? ReminderSentAt, DateTime? EscalationSentAt, Guid? ReminderNotificationId, Guid? EscalationNotificationId);

    private sealed class Harness(DataIsolationFactory factory, Clock clock, Queue queue) : IDisposable
    {
        public DataIsolationFactory Factory { get; } = factory;
        public Clock Clock { get; } = clock;
        public Queue Queue { get; } = queue;
        public static async Task<Harness> CreateAsync()
        {
            var clock = new Clock(); var queue = new Queue();
            var factory = await DataIsolationFactory.CreateAsync(configureServices: services =>
            {
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
                services.RemoveAll<IQueueService>(); services.AddSingleton<IQueueService>(queue);
            });
            try
            {
                await factory.WithDatabaseAsync(async db =>
                {
                    var task = await db.Tasks.SingleAsync(value => value.Id == 101);
                    task.CreatedAt = clock.GetUtcNow().UtcDateTime.AddDays(-1);
                    task.DueDate = clock.GetUtcNow().UtcDateTime.AddHours(-2);
                    task.ReminderEnabled = true; task.ReminderOffsetMinutes = 15;
                    task.EscalationEnabled = true; task.EscalationDelayMinutes = 30;
                    await db.SaveChangesAsync();
                });
                return new(factory, clock, queue);
            }
            catch { factory.Dispose(); throw; }
        }
        public async Task<OldOccurrence[]> AddOldOccurrencesAsync(bool leased)
        {
            var values = new List<OldOccurrence>();
            await Factory.WithDatabaseAsync(async db =>
            {
                var task = await db.Tasks.Include(value => value.Project).SingleAsync(value => value.Id == 101);
                foreach (var escalation in new[] { false, true })
                {
                    var id = Guid.NewGuid();
                    var body = JsonSerializer.Serialize(new TaskNotificationEnvelope
                    {
                        Type = escalation ? NotificationEventType.TaskOverdueEscalation : NotificationEventType.TaskReminder,
                        CorrelationId = id.ToString("N"), Reminder = new(task.Id, task.Name, task.PerformerId,
                            task.ProjectId, task.Project.Name, task.DueDate!.Value, 15, 30)
                    });
                    if (escalation) task.EscalationNotificationId = id; else task.ReminderNotificationId = id;
                    db.NotificationOutbox.Add(new NotificationOutboxMessage
                    {
                        Id = id, TaskId = task.Id, IsEscalation = escalation, Kind = NotificationOutboxKind.Reminder,
                        QueueName = "TaskReminders", Payload = body, CreatedAtUtc = Clock.GetUtcNow().UtcDateTime,
                        LockId = leased ? Guid.NewGuid() : null,
                        LockedUntilUtc = leased ? Clock.GetUtcNow().UtcDateTime.AddMinutes(5) : null
                    });
                    values.Add(new(id, body));
                }
                await db.SaveChangesAsync();
            });
            return values.ToArray();
        }
        public async Task<TaskSnapshot> ReadAsync()
        {
            TaskSnapshot? snapshot = null;
            await Factory.WithDatabaseAsync(async db =>
            {
                var task = await db.Tasks.AsNoTracking().SingleAsync(value => value.Id == 101);
                snapshot = new(task.State, task.FinishedAt, task.Version, task.ReminderSentAt, task.EscalationSentAt,
                    task.ReminderNotificationId, task.EscalationNotificationId);
            });
            return snapshot!;
        }
        public async Task<string> OutboxSnapshotAsync()
        {
            var snapshot = "";
            await Factory.WithDatabaseAsync(async db => snapshot = JsonSerializer.Serialize(
                await db.NotificationOutbox.AsNoTracking().OrderBy(value => value.Id).ToListAsync()));
            return snapshot;
        }
        public async Task RunSchedulerAsync()
        {
            using var scheduler = new TaskNotificationScheduler(Factory.Services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new ReminderOptions()), Options.Create(new RabbitMqOptions { ReminderQueueName = "TaskReminders" }),
                NullLogger<TaskNotificationScheduler>.Instance, Clock);
            await scheduler.RunOnceAsync();
        }
        public async Task AssertSuppressedAsync(OldOccurrence value)
        {
            using var scope = Factory.Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<INotificationMessageProcessor>().ProcessAsync(value.Id, value.Payload);
            Assert.True(result.Suppressed);
            Assert.False(result.Created);
        }
        public Task AssertOnlySuppressedMarkersAsync(int expected) => Factory.WithDatabaseAsync(async db =>
        {
            Assert.Empty(await db.UserNotifications.ToListAsync());
            var markers = await db.NotificationProcessingMarkers.ToListAsync();
            Assert.Equal(expected, markers.Count);
            Assert.All(markers, marker => Assert.Equal(NotificationProcessingDisposition.Suppressed, marker.Disposition));
        });
        public void Dispose() => Factory.Dispose();
    }
}
