using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DAL.Enum;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tests.Notifications;
using Xunit;

namespace Tests.Integration;

// Uses the existing guarded GUID database helper and ALL real migrations. The
// command barrier makes both HTTP transactions hold the same original task
// before their competing writes; no SQL errors or HTTP responses are mocked.
public sealed class SqlServerTaskStateConcurrencyTests
{
    [SqlServerFact]
    public Task TwoStatusCommandsWithOneVersion_CommitExactlyOneWinner() => VerifyCompetitionAsync(reminderCompetitor: false);

    [SqlServerFact]
    public Task StatusAndReminderCommandsWithOneVersion_CannotPartiallyMerge() => VerifyCompetitionAsync(reminderCompetitor: true);

    private static async Task VerifyCompetitionAsync(bool reminderCompetitor)
    {
        await using var database = await NotificationTestDatabase.CreateAsync();
        var gate = new WriteGate();
        var fixture = await SqlServerNotificationsConcurrencyTests.CreateFixtureAsync(database, gate);
        using var factory = fixture.Factory;
        using var first = factory.CreateUserClient(fixture.OwnerId);
        using var second = factory.CreateUserClient(fixture.OwnerId);
        var original = await ReadAsync(factory, fixture.TaskId);
        var originalOutbox = await OutboxAsync(factory);
        var originalDelivery = await DeliveryAsync(factory);
        var changedDue = new DateTime(2030, 10, 16, 12, 0, 0, DateTimeKind.Utc);
        gate.Arm();

        var a = first.PatchAsJsonAsync($"/api/tasks/{fixture.TaskId}/state", new { state = "Done", expectedVersion = original.Version });
        var b = reminderCompetitor
            ? second.PutAsJsonAsync($"/api/tasks/{fixture.TaskId}/reminder", new
            {
                dueDate = changedDue, reminderEnabled = true, reminderOffsetMinutes = 15,
                escalationEnabled = true, escalationDelayMinutes = 30, expectedVersion = original.Version
            })
            : second.PatchAsJsonAsync($"/api/tasks/{fixture.TaskId}/state", new { state = "InProgress", expectedVersion = original.Version });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var responses = await Task.WhenAll(a, b).WaitAsync(timeout.Token);

        Assert.Equal(2, gate.FirstWriterConnections);
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, responses.Select(value => value.StatusCode).Order().ToArray());
        var winnerIndex = Array.FindIndex(responses, value => value.StatusCode == HttpStatusCode.OK);
        var response = await responses[winnerIndex].Content.ReadFromJsonAsync<JsonElement>();
        var stored = await ReadAsync(factory, fixture.TaskId);
        Assert.NotEqual(original.Version, stored.Version);
        Assert.Equal(stored.Version, response.GetProperty("version").GetGuid());
        if (winnerIndex == 0)
        {
            Assert.Equal(TaskState.Done, stored.State);
            Assert.NotNull(stored.FinishedAt);
            Assert.Equal(original.DueDate, stored.DueDate);
            Assert.Equal(original.ReminderEnabled, stored.ReminderEnabled);
            Assert.Equal(original.EscalationEnabled, stored.EscalationEnabled);
        }
        else if (reminderCompetitor)
        {
            Assert.Equal(original.State, stored.State);
            Assert.Equal(original.FinishedAt, stored.FinishedAt);
            Assert.Equal(changedDue, stored.DueDate);
            Assert.True(stored.ReminderEnabled);
            Assert.True(stored.EscalationEnabled);
            Assert.Equal(15, stored.ReminderOffsetMinutes);
            Assert.Equal(30, stored.EscalationDelayMinutes);
        }
        else
        {
            Assert.Equal(TaskState.InProgress, stored.State);
            Assert.Null(stored.FinishedAt);
        }
        Assert.Equal(original.Name, stored.Name);
        Assert.Equal(original.Description, stored.Description);
        Assert.Equal(original.ProjectId, stored.ProjectId);
        Assert.Equal(original.PerformerId, stored.PerformerId);
        Assert.Equal(originalOutbox, await OutboxAsync(factory));
        // Reusing the losing token, including for an apparent no-op, must not
        // turn a deadlock victim into a second successful update.
        var stale = await first.PatchAsJsonAsync($"/api/tasks/{fixture.TaskId}/state", new
        {
            state = stored.State.ToString(), expectedVersion = original.Version
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(stored, await ReadAsync(factory, fixture.TaskId));
        Assert.True(originalDelivery == await DeliveryAsync(factory),
            "A state competition must preserve all pre-existing delivery/history rows from real migrations.");
        foreach (var value in responses) value.Dispose();
    }

    private static async Task<Snapshot> ReadAsync(DataIsolationFactory factory, int taskId)
    {
        Snapshot? value = null;
        await factory.WithDatabaseAsync(async db => value = await db.Tasks.AsNoTracking().Where(task => task.Id == taskId)
            .Select(task => new Snapshot(task.State, task.Version, task.FinishedAt, task.DueDate, task.ReminderEnabled,
                task.ReminderOffsetMinutes, task.EscalationEnabled, task.EscalationDelayMinutes, task.Name, task.Description,
                task.ProjectId, task.PerformerId)).SingleAsync());
        return value!;
    }
    private static async Task<string> OutboxAsync(DataIsolationFactory factory)
    {
        var snapshot = "";
        await factory.WithDatabaseAsync(async db => snapshot = JsonSerializer.Serialize(
            await db.NotificationOutbox.AsNoTracking().OrderBy(value => value.Id).ToListAsync()));
        return snapshot;
    }
    private static async Task<string> DeliveryAsync(DataIsolationFactory factory)
    {
        var snapshot = "";
        await factory.WithDatabaseAsync(async db => snapshot = JsonSerializer.Serialize(new
        {
            executed = await db.ExecutedTasks.AsNoTracking().OrderBy(value => value.Id).ToListAsync(),
            inbox = await db.UserNotifications.AsNoTracking().OrderBy(value => value.Id).ToListAsync(),
            markers = await db.NotificationProcessingMarkers.AsNoTracking().OrderBy(value => value.MessageId).ToListAsync()
        }));
        return snapshot;
    }
    private sealed record Snapshot(TaskState State, Guid Version, DateTime? FinishedAt, DateTime? DueDate,
        bool ReminderEnabled, int? ReminderOffsetMinutes, bool EscalationEnabled, int? EscalationDelayMinutes,
        string Name, string Description, int ProjectId, int PerformerId);

    private sealed class WriteGate : DbCommandInterceptor
    {
        private readonly ConcurrentDictionary<Guid, byte> _connections = new();
        private readonly TaskCompletionSource _bothWriters = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        private bool _armed;
        public int FirstWriterConnections => _connections.Count;
        public void Arm() => _armed = true;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await WaitAsync(command, cancellationToken);
            return result;
        }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await WaitAsync(command, cancellationToken);
            return result;
        }
        private async Task WaitAsync(DbCommand command, CancellationToken ct)
        {
            if (!_armed || !command.CommandText.Contains("UPDATE [Tasks]", StringComparison.OrdinalIgnoreCase)) return;
            // A bounded deadlock retry must run normally rather than wait for a
            // nonexistent third contender.
            var arrival = Interlocked.Increment(ref _arrivals);
            if (arrival > 2) return;
            Assert.NotNull(command.Transaction);
            Assert.IsType<SqlConnection>(command.Connection);
            _connections.TryAdd(((SqlConnection)command.Connection!).ClientConnectionId, 0);
            if (arrival == 2) _bothWriters.TrySetResult();
            await _bothWriters.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
    }
}
