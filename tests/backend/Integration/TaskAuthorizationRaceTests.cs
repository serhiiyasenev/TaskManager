using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Tests.Integration;

/// <summary>
/// Same-transaction faults at the final command boundary prove final permission
/// revalidation and rollback. They do not substitute for SQL Server lock tests.
/// </summary>
public sealed class TaskAuthorizationRaceTests
{
    [Theory]
    [InlineData("update")]
    [InlineData("reminder")]
    [InlineData("delete")]
    [InlineData("state")]
    public async Task CallerMembershipRevokedAtWrite_DoesNotCommitTaskOrOutboxChanges(string operation)
    {
        var fault = new TaskWriteInterleaving(operation == "delete" ? "DELETE" : "UPDATE", 101);
        using var factory = await DataIsolationFactory.CreateAsync(fault);
        await AddClaimedOutboxAsync(factory);
        using var caller = factory.CreateUserClient(101);
        var version = await VersionAsync(caller);
        var snapshot = await SnapshotAsync(factory);
        fault.Arm();

        var response = await MutateAsync(caller, operation, version);

        Assert.True(fault.Injected);
        AssertDenied(response);
        Assert.Equal(snapshot, await SnapshotAsync(factory));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("state")]
    public async Task AdministratorRoleRevokedAtWrite_DoesNotCommitTaskChange(string operation)
    {
        var fault = new TaskWriteInterleaving("UPDATE", 105, revokeRole: true);
        using var factory = await DataIsolationFactory.CreateAsync(fault);
        using var caller = factory.CreateUserClient(105, isAdmin: true);
        var version = await VersionAsync(caller);
        var snapshot = await SnapshotAsync(factory);
        fault.Arm();

        var response = await MutateAsync(caller, operation, version);

        Assert.True(fault.Injected);
        AssertDenied(response);
        Assert.Equal(snapshot, await SnapshotAsync(factory));
    }

    [Fact]
    public async Task ProjectMovedToForeignTeamAtStateWrite_DoesNotCommitTaskOrOccurrenceChanges()
    {
        var fault = new TaskWriteInterleaving("UPDATE", 101, moveProject: true);
        using var factory = await DataIsolationFactory.CreateAsync(fault);
        await AddClaimedOutboxAsync(factory);
        using var caller = factory.CreateUserClient(101);
        var version = await VersionAsync(caller);
        var snapshot = await SnapshotAsync(factory);
        fault.Arm();

        var response = await MutateAsync(caller, "state", version);

        Assert.True(fault.Injected);
        AssertDenied(response);
        Assert.Equal(snapshot, await SnapshotAsync(factory));
        await factory.WithDatabaseAsync(async db => Assert.Equal(101,
            (await db.Projects.AsNoTracking().SingleAsync(project => project.Id == 101)).TeamId));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("create")]
    public async Task AssigneeLeavesAtWrite_TaskAssignmentDoesNotCommit(string operation)
    {
        var fault = new TaskWriteInterleaving(operation == "create" ? "INSERT" : "UPDATE", 102);
        using var factory = await DataIsolationFactory.CreateAsync(fault);
        using var caller = factory.CreateUserClient(101);
        var version = await VersionAsync(caller);
        var snapshot = await SnapshotAsync(factory);
        fault.Arm();

        var body = new { projectId = 101, performerId = 102, name = "Raced new assignment", description = "Must not persist", state = 0, expectedVersion = version };
        var response = operation == "create" ? await caller.PostAsJsonAsync("/api/tasks", body)
            : await caller.PutAsJsonAsync("/api/tasks/101", body);

        Assert.True(fault.Injected);
        AssertDenied(response);
        Assert.Equal(snapshot, await SnapshotAsync(factory));
        await factory.WithDatabaseAsync(async db => Assert.False(await db.Tasks.AnyAsync(task => task.Name == "Raced new assignment")));
    }

    private static void AssertDenied(HttpResponseMessage response) => Assert.Contains(response.StatusCode,
        new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.Conflict });

    private static async Task<Guid> VersionAsync(HttpClient caller) =>
        (await caller.GetFromJsonAsync<JsonElement>("/api/tasks/101")).GetProperty("version").GetGuid();

    private static async Task<HttpResponseMessage> MutateAsync(HttpClient caller, string operation, Guid version)
    {
        if (operation == "delete") caller.DefaultRequestHeaders.Add("If-Match", $"\"{version}\"");
        return operation switch
        {
            "update" => await caller.PutAsJsonAsync("/api/tasks/101", new { projectId = 101, performerId = 101, name = "Raced edit", description = "Must not persist", state = 0, expectedVersion = version }),
            "reminder" => await caller.PutAsJsonAsync("/api/tasks/101/reminder", new { dueDate = DateTime.UtcNow.AddDays(2), reminderEnabled = true, reminderOffsetMinutes = 15, escalationEnabled = true, escalationDelayMinutes = 30, expectedVersion = version }),
            "state" => await caller.PatchAsJsonAsync("/api/tasks/101/state", new { state = "Done", expectedVersion = version }),
            _ => await caller.DeleteAsync("/api/tasks/101")
        };
    }

    private static async Task<string> SnapshotAsync(DataIsolationFactory factory)
    {
        string result = "";
        await factory.WithDatabaseAsync(async db => result = JsonSerializer.Serialize(new
        {
            tasks = await db.Tasks.AsNoTracking().OrderBy(task => task.Id).Select(task => new
            {
                task.Id, task.Version, task.Name, task.Description, task.ProjectId, task.PerformerId, task.State, task.FinishedAt,
                task.DueDate, task.ReminderEnabled, task.ReminderOffsetMinutes, task.ReminderNotificationId,
                task.ReminderSentAt, task.EscalationEnabled, task.EscalationDelayMinutes, task.EscalationNotificationId, task.EscalationSentAt
            }).ToListAsync(),
            outbox = await db.NotificationOutbox.AsNoTracking().OrderBy(value => value.Id).ToListAsync()
        }));
        return result;
    }

    private static async Task AddClaimedOutboxAsync(DataIsolationFactory factory) => await factory.WithDatabaseAsync(async db =>
    {
        db.NotificationOutbox.Add(new DAL.Entities.NotificationOutboxMessage
        {
            Id = Guid.NewGuid(), TaskId = 101, QueueName = "TaskReminders", Payload = "{}", CreatedAtUtc = DateTime.UtcNow,
            LockId = Guid.NewGuid(), LockedUntilUtc = DateTime.UtcNow.AddMinutes(5)
        });
        await db.SaveChangesAsync();
    });

    private sealed class TaskWriteInterleaving(string verb, int subjectId, bool revokeRole = false, bool moveProject = false) : DbCommandInterceptor
    {
        private bool _armed;
        public bool Injected { get; private set; }
        public void Arm() => _armed = true;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await InjectAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await InjectAsync(command, cancellationToken);
            return result;
        }

        private async Task InjectAsync(DbCommand command, CancellationToken ct)
        {
            if (!_armed || Injected || !command.CommandText.TrimStart().StartsWith(verb, StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Split("WHERE", 2, StringSplitOptions.None)[0].Contains("\"Tasks\"", StringComparison.Ordinal)) return;
            Injected = true;
            await using var change = command.Connection!.CreateCommand();
            change.Transaction = command.Transaction;
            change.CommandText = moveProject ? "UPDATE \"Projects\" SET \"TeamId\" = 102 WHERE \"Id\" = @id"
                : revokeRole ? "DELETE FROM \"AspNetUserRoles\" WHERE \"UserId\" = @id"
                : "UPDATE \"AspNetUsers\" SET \"TeamId\" = NULL, \"ConcurrencyStamp\" = @stamp WHERE \"Id\" = @id";
            var id = change.CreateParameter(); id.ParameterName = "@id"; id.Value = subjectId; change.Parameters.Add(id);
            if (!revokeRole && !moveProject)
            {
                var stamp = change.CreateParameter(); stamp.ParameterName = "@stamp"; stamp.Value = Guid.NewGuid().ToString(); change.Parameters.Add(stamp);
            }
            Assert.Equal(1, await change.ExecuteNonQueryAsync(ct));
        }
    }
}
