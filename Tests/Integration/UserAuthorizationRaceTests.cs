using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Tests.Integration;

// Same-transaction command-boundary faults prove fresh administrative rights
// and rollback. They do not substitute for concurrent SQL Server lock tests.
public sealed class UserAuthorizationRaceTests
{
    [Fact]
    public async Task AdminRoleRevokedAtForeignProfileUpdate_CannotChangeTheAccount()
    {
        var fault = new UserWriteInterleaving("UPDATE");
        using var factory = await DataIsolationFactory.CreateAsync(fault);
        using var caller = factory.CreateUserClient(105, isAdmin: true);
        var before = await SafeSnapshotAsync(factory);
        fault.Arm();

        var response = await caller.PutAsJsonAsync("/api/users/103", new
        {
            teamId = 102, email = "member103@example.test", userName = "member103",
            firstName = "Must not commit", lastName = "After role revocation", birthDay = "1995-01-01"
        });

        Assert.True(fault.Injected, "The originally authorized foreign-profile UPDATE must reach its command boundary.");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await SafeSnapshotAsync(factory));
    }

    [Fact]
    public async Task AdminRoleRevokedAtForeignAccountDelete_CannotRemoveTheAccount()
    {
        var fault = new UserWriteInterleaving("DELETE");
        using var factory = await DataIsolationFactory.CreateAsync(fault);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.True(await db.Users.AnyAsync(user => user.Id == 104));
            Assert.False(await db.Projects.AnyAsync(project => project.AuthorId == 104));
            Assert.False(await db.Tasks.AnyAsync(task => task.PerformerId == 104));
            Assert.False(await db.Teams.AnyAsync(team => team.OwnerId == 104));
        });
        using var caller = factory.CreateUserClient(105, isAdmin: true);
        var before = await SafeSnapshotAsync(factory);
        fault.Arm();

        var response = await caller.DeleteAsync("/api/users/104");

        Assert.True(fault.Injected, "Dependency-free account deletion must reach DELETE rather than fail on an unrelated FK.");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await SafeSnapshotAsync(factory));
    }

    private static async Task<string> SafeSnapshotAsync(DataIsolationFactory factory)
    {
        var snapshot = "";
        await factory.WithDatabaseAsync(async db => snapshot = JsonSerializer.Serialize(new
        {
            // Deliberately excludes credentials, tokens, hashes and every stamp.
            users = await db.Users.AsNoTracking().OrderBy(user => user.Id).Select(user => new
            {
                user.Id, user.TeamId, user.UserName, user.FirstName, user.LastName,
                user.RegisteredAt, user.BirthDay, user.EmailConfirmed
            }).ToListAsync(),
            projects = await db.Projects.CountAsync(), tasks = await db.Tasks.CountAsync(),
            teams = await db.Teams.CountAsync(), roleMemberships = await db.UserRoles.CountAsync()
        }));
        return snapshot;
    }

    private sealed class UserWriteInterleaving(string verb) : DbCommandInterceptor
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
                || !command.CommandText.Split("WHERE", 2, StringSplitOptions.None)[0].Contains("\"AspNetUsers\"", StringComparison.Ordinal)) return;
            await using var revoke = command.Connection!.CreateCommand();
            revoke.Transaction = command.Transaction;
            revoke.CommandText = "DELETE FROM \"AspNetUserRoles\" WHERE \"UserId\" = @actor";
            var actor = revoke.CreateParameter(); actor.ParameterName = "@actor"; actor.Value = 105;
            revoke.Parameters.Add(actor);
            Assert.Equal(1, await revoke.ExecuteNonQueryAsync(ct));
            Injected = true;
        }
    }
}
