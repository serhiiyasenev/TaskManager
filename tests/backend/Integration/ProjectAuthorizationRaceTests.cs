using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Tests.Integration;

// A same-connection permission change at the actual write boundary proves final
// rights validation and rollback. It is not a concurrent SQL Server lock proof.
public sealed class ProjectAuthorizationRaceTests
{
    [Theory]
    [InlineData("create", "INSERT")]
    [InlineData("update", "UPDATE")]
    [InlineData("delete", "DELETE")]
    public async Task MemberLosesMembershipAtWrite_ProjectMutationDoesNotCommit(string operation, string verb)
    {
        var fault = new ProjectWriteInterleaving(verb, actorId: 101, revokeRole: false);
        using var factory = await DataIsolationFactory.CreateAsync(fault);
        // A dependency-free authored project ensures DELETE reaches its actual
        // command rather than passing merely because of the tasks precheck/FK.
        await AddEmptyAuthoredProjectAsync(factory);
        using var caller = factory.CreateUserClient(101);
        var before = await SnapshotAsync(factory);
        fault.Arm();

        var response = operation switch
        {
            "create" => await caller.PostAsJsonAsync("/api/projects", Body(101)),
            "update" => await caller.PutAsJsonAsync("/api/projects/105", Body(101)),
            _ => await caller.DeleteAsync("/api/projects/105")
        };

        Assert.True(fault.Injected, "The request must reach the project command after its original permission check.");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(factory));
    }

    [Fact]
    public async Task AdministratorRoleRevokedAtForeignProjectWrite_CannotCommitWithStaleClaims()
    {
        var fault = new ProjectWriteInterleaving("UPDATE", actorId: 105, revokeRole: true);
        using var factory = await DataIsolationFactory.CreateAsync(fault);
        using var caller = factory.CreateUserClient(105, isAdmin: true);
        var before = await SnapshotAsync(factory);
        fault.Arm();

        var response = await caller.PutAsJsonAsync("/api/projects/102", Body(102));

        Assert.True(fault.Injected, "Persisted admin rights must be revoked during the actual authorized UPDATE.");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(factory));
    }

    private static object Body(int teamId) => new
    {
        teamId, name = "Must not commit after access loss", description = "Boundary permission regression",
        deadline = new DateTime(2030, 10, 16, 12, 0, 0, DateTimeKind.Utc)
    };

    private static Task AddEmptyAuthoredProjectAsync(DataIsolationFactory factory) => factory.WithDatabaseAsync(async db =>
    {
        db.Projects.Add(new DAL.Entities.Project
        {
            Id = 105, TeamId = 101, AuthorId = 101, Name = "Empty authored project", Description = "Safe delete fixture",
            CreatedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            Deadline = new DateTime(2030, 10, 20, 12, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
    });

    private static async Task<string> SnapshotAsync(DataIsolationFactory factory)
    {
        var snapshot = "";
        await factory.WithDatabaseAsync(async db => snapshot = JsonSerializer.Serialize(new
        {
            projects = await db.Projects.AsNoTracking().OrderBy(project => project.Id).Select(project => new
            {
                project.Id, project.TeamId, project.AuthorId, project.Name, project.Description, project.CreatedAt, project.Deadline
            }).ToListAsync(),
            tasks = await db.Tasks.AsNoTracking().OrderBy(task => task.Id).Select(task => new
            {
                task.Id, task.ProjectId, task.PerformerId, task.Name, task.Description, task.State, task.Version,
                task.ReminderNotificationId, task.EscalationNotificationId
            }).ToListAsync(),
            outbox = await db.NotificationOutbox.AsNoTracking().OrderBy(message => message.Id).ToListAsync()
        }));
        return snapshot;
    }

    private sealed class ProjectWriteInterleaving(string verb, int actorId, bool revokeRole) : DbCommandInterceptor
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
                || !command.CommandText.Split("WHERE", 2, StringSplitOptions.None)[0].Contains("\"Projects\"", StringComparison.Ordinal)) return;
            await using var revoke = command.Connection!.CreateCommand();
            revoke.Transaction = command.Transaction;
            revoke.CommandText = revokeRole
                ? "DELETE FROM \"AspNetUserRoles\" WHERE \"UserId\" = @id"
                : "UPDATE \"AspNetUsers\" SET \"TeamId\" = NULL, \"ConcurrencyStamp\" = @stamp WHERE \"Id\" = @id";
            var id = revoke.CreateParameter(); id.ParameterName = "@id"; id.Value = actorId; revoke.Parameters.Add(id);
            if (!revokeRole)
            {
                var stamp = revoke.CreateParameter(); stamp.ParameterName = "@stamp";
                stamp.Value = Guid.NewGuid().ToString(); revoke.Parameters.Add(stamp);
            }
            Assert.Equal(1, await revoke.ExecuteNonQueryAsync(ct));
            Injected = true;
        }
    }
}
