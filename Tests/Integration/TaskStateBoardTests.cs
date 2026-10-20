using System.Net;
using System.Net.Http.Json;
using DAL.Enum;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TaskEntity = DAL.Entities.Task;

namespace Tests.Integration;

// Actual HTTP/model binding, access queries, SQLite transaction and task writes.
// The existing factory's synthetic principal isolates business authorization;
// these cases do not claim cookie authentication or real SQL Server coverage.
public sealed class TaskStateBoardTests
{
    [Fact]
    public async Task Administrator_CanChangeLegacyDepartedAssignmentStateWithoutRewritingOtherFields()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        await PrepareAsync(factory, departedAssignee: true);
        using var client = factory.CreateUserClient(105, isAdmin: true);
        var before = await ReadAsync(factory);

        var response = await client.PatchAsJsonAsync("/api/tasks/101/state",
            new { state = "InProgress", expectedVersion = before.Version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await ReadAsync(factory);
        Assert.Equal(TaskState.InProgress, after.State);
        Assert.NotEqual(before.Version, after.Version);
        Assert.Null(after.FinishedAt);
        AssertPreserved(before, after);
        Assert.Equal(before.ReminderNotificationId, after.ReminderNotificationId);
        Assert.Equal(before.EscalationNotificationId, after.EscalationNotificationId);
    }

    [Fact]
    public async Task FullUpdate_StillRejectsTheSameInvalidCurrentAssignment()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        await PrepareAsync(factory, departedAssignee: true);
        using var client = factory.CreateUserClient(105, isAdmin: true);
        var before = await ReadAsync(factory);

        var response = await client.PutAsJsonAsync("/api/tasks/101", new
        {
            before.ProjectId, before.PerformerId, before.Name, before.Description,
            state = "InProgress", before.DueDate, before.ReminderEnabled, before.ReminderOffsetMinutes,
            before.EscalationEnabled, before.EscalationDelayMinutes, expectedVersion = before.Version
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ReadAsync(factory));
    }

    [Theory]
    [InlineData("foreign-member", 101, 102, false)]
    [InlineData("revoked-membership", 101, 101, false)]
    [InlineData("revoked-admin-role", 105, 101, true)]
    public async Task StateWrite_DoesNotBypassCurrentProjectPermissions(string scenario, int actor, int taskId, bool adminClaim)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        var before = await ReadAsync(factory, taskId);
        await factory.WithDatabaseAsync(async db =>
        {
            if (scenario == "revoked-membership") (await db.Users.SingleAsync(user => user.Id == actor)).TeamId = null;
            if (scenario == "revoked-admin-role") db.UserRoles.RemoveRange(await db.UserRoles.Where(role => role.UserId == actor).ToListAsync());
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateUserClient(actor, adminClaim);

        var response = await client.PatchAsJsonAsync($"/api/tasks/{taskId}/state",
            new { state = "Done", expectedVersion = before.Version });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await ReadAsync(factory, taskId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mutation_WithoutVersion_IsRejectedForBrowserAndExplicitBearer(bool explicitBearer)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(105, isAdmin: true);
        if (!explicitBearer)
        {
            // Retain the factory principal, remove only its explicit-bearer marker.
            client.DefaultRequestHeaders.Authorization = null;
            var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf");
            Assert.NotNull(csrf);
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.RequestToken);
            client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        }
        var before = await ReadAsync(factory);

        var response = await client.PatchAsJsonAsync("/api/tasks/101/state", new { state = "Done" });

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        Assert.Equal(before, await ReadAsync(factory));
    }

    [Fact]
    public async Task StrongIfMatch_AloneAllowsOneWriteThenRejectsTheSameStaleVersion()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        var before = await ReadAsync(factory);
        client.DefaultRequestHeaders.Add("If-Match", $"\"{before.Version:D}\"");

        var first = await client.PatchAsJsonAsync("/api/tasks/101/state", new { state = "InProgress" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var changed = await ReadAsync(factory);
        Assert.Equal(TaskState.InProgress, changed.State);
        Assert.NotEqual(before.Version, changed.Version);
        AssertPreserved(before, changed);

        var stale = await client.PatchAsJsonAsync("/api/tasks/101/state", new { state = "Done" });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(changed, await ReadAsync(factory));
    }

    [Fact]
    public async Task SameTerminalState_IsANoOpPreservingVersionAndOriginalFinishedAt()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        await PrepareAsync(factory);
        await factory.WithDatabaseAsync(async db =>
        {
            var task = await db.Tasks.SingleAsync(value => value.Id == 101);
            task.State = TaskState.Done;
            task.FinishedAt = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
            task.ReminderNotificationId = null;
            task.EscalationNotificationId = null;
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateUserClient(101);
        var before = await ReadAsync(factory);

        var response = await client.PatchAsJsonAsync("/api/tasks/101/state", new { state = "Done", expectedVersion = before.Version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await ReadAsync(factory));
    }

    [Theory]
    [InlineData("invalid-text-state")]
    [InlineData("invalid-numeric-state")]
    [InlineData("missing-state")]
    [InlineData("invalid-version")]
    [InlineData("empty-version")]
    [InlineData("stale-version")]
    [InlineData("header-body-version-mismatch")]
    public async Task InvalidOrStaleStateRequest_CannotChangeTheTask(string scenario)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(105, isAdmin: true);
        var before = await ReadAsync(factory);
        var body = new Dictionary<string, object?> { ["state"] = "Done", ["expectedVersion"] = before.Version };
        if (scenario == "invalid-text-state") body["state"] = "UnknownState";
        if (scenario == "invalid-numeric-state") body["state"] = 999;
        if (scenario == "missing-state") body.Remove("state");
        if (scenario == "invalid-version") body["expectedVersion"] = "not-a-version";
        if (scenario == "empty-version") body["expectedVersion"] = Guid.Empty;
        if (scenario == "stale-version") body["expectedVersion"] = Guid.NewGuid();
        if (scenario == "header-body-version-mismatch") client.DefaultRequestHeaders.Add("If-Match", $"\"{Guid.NewGuid():D}\"");

        var response = await client.PatchAsJsonAsync("/api/tasks/101/state", body);

        Assert.Equal(scenario == "stale-version" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ReadAsync(factory));
    }

    [Fact]
    public async Task BodyInjection_CannotRewriteAssignmentOrOtherTaskBusinessFields()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        await PrepareAsync(factory);
        using var client = factory.CreateUserClient(105, isAdmin: true);
        var before = await ReadAsync(factory);

        var response = await client.PatchAsJsonAsync("/api/tasks/101/state", new
        {
            state = "InProgress", expectedVersion = before.Version,
            projectId = 102, performerId = 103, name = "Injected replacement", description = "Injected description",
            dueDate = "2040-01-01T00:00:00Z", reminderEnabled = false, reminderOffsetMinutes = 1,
            escalationEnabled = false, escalationDelayMinutes = 1, reminderSentAt = "2040-01-01T00:00:00Z",
            createdAt = "2040-01-01T00:00:00Z", reminderNotificationId = Guid.NewGuid(), escalationNotificationId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await ReadAsync(factory);
        Assert.Equal(TaskState.InProgress, after.State);
        AssertPreserved(before, after);
        Assert.Equal(before.ReminderNotificationId, after.ReminderNotificationId);
        Assert.Equal(before.EscalationNotificationId, after.EscalationNotificationId);
    }

    [Theory]
    [InlineData("Done", TaskState.Done)]
    [InlineData("Canceled", TaskState.Canceled)]
    public async Task TerminalAndReopenedState_PreserveSettingsAndCancelExistingOccurrences(string terminal, TaskState expectedState)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        await PrepareAsync(factory);
        using var client = factory.CreateUserClient(101);
        var before = await ReadAsync(factory);
        var started = DateTime.UtcNow;

        var terminalResponse = await client.PatchAsJsonAsync("/api/tasks/101/state", new { state = terminal, expectedVersion = before.Version });

        Assert.Equal(HttpStatusCode.OK, terminalResponse.StatusCode);
        var closed = await ReadAsync(factory);
        Assert.Equal(expectedState, closed.State);
        Assert.NotNull(closed.FinishedAt);
        Assert.InRange(closed.FinishedAt.Value, started.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.NotEqual(before.Version, closed.Version);
        Assert.Null(closed.ReminderNotificationId);
        Assert.Null(closed.EscalationNotificationId);
        AssertPreserved(before, closed);

        var reopenResponse = await client.PatchAsJsonAsync("/api/tasks/101/state", new { state = "ToDo", expectedVersion = closed.Version });

        Assert.Equal(HttpStatusCode.OK, reopenResponse.StatusCode);
        var reopened = await ReadAsync(factory);
        Assert.Equal(TaskState.ToDo, reopened.State);
        Assert.Null(reopened.FinishedAt);
        Assert.NotEqual(closed.Version, reopened.Version);
        Assert.Null(reopened.ReminderNotificationId);
        Assert.Null(reopened.EscalationNotificationId);
        AssertPreserved(before, reopened);
    }

    private static async Task PrepareAsync(DataIsolationFactory factory, bool departedAssignee = false) =>
        await factory.WithDatabaseAsync(async db =>
        {
            var task = await db.Tasks.SingleAsync(value => value.Id == 101);
            task.CreatedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
            task.DueDate = new DateTime(2030, 10, 19, 9, 0, 0, DateTimeKind.Utc);
            task.ReminderEnabled = true; task.ReminderOffsetMinutes = 60;
            task.EscalationEnabled = true; task.EscalationDelayMinutes = 120;
            task.ReminderSentAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
            task.EscalationSentAt = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
            task.ReminderNotificationId = Guid.NewGuid(); task.EscalationNotificationId = Guid.NewGuid();
            if (departedAssignee) (await db.Users.SingleAsync(user => user.Id == task.PerformerId)).TeamId = null;
            await db.SaveChangesAsync();
        });

    private static async Task<Snapshot> ReadAsync(DataIsolationFactory factory, int taskId = 101)
    {
        Snapshot? result = null;
        await factory.WithDatabaseAsync(async db =>
        {
            result = Snapshot.From(await db.Tasks.AsNoTracking().SingleAsync(task => task.Id == taskId));
        });
        return result!;
    }

    private static void AssertPreserved(Snapshot before, Snapshot after)
    {
        Assert.Equal(before with { State = after.State, FinishedAt = after.FinishedAt, Version = after.Version,
            ReminderNotificationId = after.ReminderNotificationId, EscalationNotificationId = after.EscalationNotificationId }, after);
    }

    private sealed record CsrfResponse(string RequestToken);
    private sealed record Snapshot(int Id, int ProjectId, int PerformerId, string Name, string Description, DateTime CreatedAt,
        TaskState State, DateTime? FinishedAt, DateTime? DueDate, bool ReminderEnabled, int? ReminderOffsetMinutes,
        bool EscalationEnabled, int? EscalationDelayMinutes, DateTime? ReminderSentAt, DateTime? EscalationSentAt,
        Guid? ReminderNotificationId, Guid? EscalationNotificationId, Guid Version)
    {
        public static Snapshot From(TaskEntity task) => new(task.Id, task.ProjectId, task.PerformerId, task.Name, task.Description, task.CreatedAt,
            task.State, task.FinishedAt, task.DueDate, task.ReminderEnabled, task.ReminderOffsetMinutes,
            task.EscalationEnabled, task.EscalationDelayMinutes, task.ReminderSentAt, task.EscalationSentAt,
            task.ReminderNotificationId, task.EscalationNotificationId, task.Version);
    }
}
