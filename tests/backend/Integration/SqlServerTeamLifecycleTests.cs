using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using System.Text.Json;
using DAL.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Integration;

public sealed class SqlServerTeamLifecycleTests
{
    [SqlServerFact]
    public async Task OwnedTeamInvitationLifecycle_RealSqlServerEnforcesRecipientAndPreservesAccounts()
    {
        // The supplied server is used only to create an isolated test database.
        // The application's existing/default database is never opened or changed.
        await using var database = await DisposableHttpDatabase.CreateAsync();
        var lateFailure = new FinalInvitationWriteFailure();
        using var factory = await DataIsolationFactory.CreateAsync(lateFailure, sqlConnection: database.ConnectionString);
        var ownerId = 0;
        var receiverId = 0;
        var outsiderId = 0;
        await factory.WithDatabaseAsync(async db =>
        {
            var owner = Account("sql.owner@example.test");
            var receiver = Account("sql.receiver@example.test");
            var outsider = Account("sql.outsider@example.test");
            db.Users.AddRange(owner, receiver, outsider);
            await db.SaveChangesAsync();
            ownerId = owner.Id; receiverId = receiver.Id; outsiderId = outsider.Id;
        });
        using var ownerClient = factory.CreateUserClient(ownerId);
        using var receiverClient = factory.CreateUserClient(receiverId);
        using var outsiderClient = factory.CreateUserClient(outsiderId);
        var created = await ownerClient.PostAsJsonAsync("/api/teams", new { name = "SQL lifecycle team" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var teamId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var invitation = await InviteAsync(ownerClient, teamId);

        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.PostAsJsonAsync("/api/team-invitations/accept", new
        {
            token = invitation.Token
        })).StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Null((await db.Users.SingleAsync(user => user.Id == outsiderId)).TeamId);
            Assert.Null((await db.Set<TeamInvitation>().SingleAsync(value => value.Id == invitation.Id)).AcceptedAtUtc);
            Assert.Equal(teamId, (await db.Users.SingleAsync(user => user.Id == ownerId)).TeamId);
        });

        Assert.Equal(HttpStatusCode.OK, (await receiverClient.PostAsJsonAsync("/api/team-invitations/accept", new
        {
            token = invitation.Token
        })).StatusCode);
        var visible = await receiverClient.GetAsync($"/api/teams/{teamId}");
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        // Exercise SQL's same-team conditional receiver update with a fresh
        // invitation, rather than only replaying an already consumed token.
        var sameTeamInvitation = await InviteAsync(ownerClient, teamId);
        Assert.Equal(HttpStatusCode.OK, (await receiverClient.PostAsJsonAsync("/api/team-invitations/accept", new
        {
            token = sameTeamInvitation.Token
        })).StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            var accepted = await db.Set<TeamInvitation>().SingleAsync(value => value.Id == invitation.Id);
            Assert.Equal(receiverId, accepted.AcceptedByUserId);
            Assert.NotNull(accepted.AcceptedAtUtc);
            Assert.Equal(teamId, (await db.Users.SingleAsync(user => user.Id == receiverId)).TeamId);
        });

        Assert.Equal(HttpStatusCode.OK, (await ownerClient.PutAsJsonAsync($"/api/teams/{teamId}", new
        {
            name = "SQL renamed team"
        })).StatusCode);
        var renamed = await receiverClient.GetAsync($"/api/teams/{teamId}");
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("SQL renamed team", (await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await receiverClient.PostAsync("/api/teams/leave", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await receiverClient.GetAsync($"/api/teams/{teamId}")).StatusCode);

        // A database failure at the last invitation write occurs after the
        // receiver membership CAS. SQL rollback must undo that earlier write.
        var interrupted = await InviteAsync(ownerClient, teamId);
        string? previousStamp = null;
        await factory.WithDatabaseAsync(async db =>
        {
            previousStamp = (await db.Users.SingleAsync(user => user.Id == receiverId)).ConcurrencyStamp;
        });
        lateFailure.Arm();
        Assert.Equal(HttpStatusCode.Conflict, (await receiverClient.PostAsJsonAsync("/api/team-invitations/accept", new
        {
            token = interrupted.Token
        })).StatusCode);
        Assert.True(lateFailure.Injected, "The late SQL write failure must be exercised.");
        await factory.WithDatabaseAsync(async db =>
        {
            var receiver = await db.Users.SingleAsync(user => user.Id == receiverId);
            Assert.Null(receiver.TeamId);
            Assert.Equal(previousStamp, receiver.ConcurrencyStamp);
            Assert.Null((await db.Set<TeamInvitation>().SingleAsync(value => value.Id == interrupted.Id)).AcceptedAtUtc);
        });

        var revoked = await InviteAsync(ownerClient, teamId);
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.DeleteAsync($"/api/teams/{teamId}/invitations/{revoked.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await receiverClient.PostAsJsonAsync("/api/team-invitations/accept", new
        {
            token = revoked.Token
        })).StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            var record = await db.Set<TeamInvitation>().SingleAsync(value => value.Id == revoked.Id);
            Assert.NotNull(record.RevokedAtUtc);
            Assert.Null(record.AcceptedAtUtc);
            Assert.Null((await db.Users.SingleAsync(user => user.Id == receiverId)).TeamId);
        });
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.DeleteAsync($"/api/teams/{teamId}")).StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.False(await db.Teams.AnyAsync(team => team.Id == teamId));
            Assert.Empty(await db.Set<TeamInvitation>().ToListAsync());
            Assert.Equal(3, await db.Users.CountAsync());
            Assert.Null((await db.Users.SingleAsync(user => user.Id == ownerId)).TeamId);
            Assert.Null((await db.Users.SingleAsync(user => user.Id == receiverId)).TeamId);
        });
    }

    private static User Account(string email) => new(email, "SQL", "Member", email)
    {
        EmailConfirmed = true, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(),
        RegisteredAt = DateTime.UtcNow, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
    };

    private static async System.Threading.Tasks.Task<Invitation> InviteAsync(HttpClient owner, int teamId)
    {
        var response = await owner.PostAsJsonAsync($"/api/teams/{teamId}/invitations", new { email = "sql.receiver@example.test" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new(result.GetProperty("id").GetGuid(), result.GetProperty("token").GetString()!);
    }

    private sealed record Invitation(Guid Id, string Token);

    private sealed class FinalInvitationWriteFailure : DbCommandInterceptor
    {
        private bool _armed;
        public bool Injected { get; private set; }
        public void Arm() => _armed = true;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_armed && !Injected && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("[TeamInvitations]", StringComparison.Ordinal) &&
                command.CommandText.Contains("[AcceptedAtUtc]", StringComparison.Ordinal))
            {
                Injected = true;
                throw new DbUpdateException("Synthetic late invitation write failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DisposableHttpDatabase(string serverConnection, string name) : IAsyncDisposable
    {
        private const string Prefix = "taskmanager_http_test_";
        public string ConnectionString { get; } = new SqlConnectionStringBuilder(serverConnection) { InitialCatalog = name }.ConnectionString;

        public static async System.Threading.Tasks.Task<DisposableHttpDatabase> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("TASKMANAGER_TEST_SQL_SERVER");
            if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("The isolated SQL test server is not configured.");
            var server = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
            var name = Prefix + Guid.NewGuid().ToString("N");
            ValidateName(name);
            await using var connection = new SqlConnection(server.ConnectionString);
            await connection.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{name}]", connection);
            await create.ExecuteNonQueryAsync();
            return new(server.ConnectionString, name);
        }

        public async ValueTask DisposeAsync()
        {
            ValidateName(name);
            // Clear only this database's pooled connections. Parallel migration
            // tests and the caller's own database pools are left undisturbed.
            using (var owned = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(owned);
            await using var connection = new SqlConnection(serverConnection);
            await connection.OpenAsync();
            await using var drop = new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", connection);
            await drop.ExecuteNonQueryAsync();
        }

        private static void ValidateName(string candidate)
        {
            if (!candidate.StartsWith(Prefix, StringComparison.Ordinal) || candidate.Length != Prefix.Length + 32 ||
                !Guid.TryParseExact(candidate[Prefix.Length..], "N", out _))
                throw new InvalidOperationException("Refusing operations outside the disposable HTTP test database namespace.");
        }
    }
}
