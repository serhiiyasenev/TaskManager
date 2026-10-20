using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Tests.Integration;

/// <summary>
/// Deterministic authorization interleavings at command boundaries. These prove
/// final write guards; real SQL Server tests are still needed to prove locking.
/// </summary>
public sealed class TeamAuthorizationRaceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invitation_IssuerMembershipRevokedBeforeConsume_DoesNotCommit(bool receiverAlreadyInTeam)
    {
        var receiverId = receiverAlreadyInTeam ? 102 : 104;
        var fault = new IssuerRevocationInterleaving(101, receiverId,
            receiverAlreadyInTeam ? RevocationPoint.ReceiverSnapshot : RevocationPoint.ReceiverJoined);
        using var factory = await DataIsolationFactory.CreateAsync(fault);
        await AssignOwnerAsync(factory);
        using var owner = factory.CreateUserClient(101);
        var created = await owner.PostAsJsonAsync("/api/teams/101/invitations", new { email = $"member{receiverId}@example.test" });
        created.EnsureSuccessStatusCode();
        var invitation = await created.Content.ReadFromJsonAsync<JsonElement>();
        var invitationId = invitation.GetProperty("id").GetGuid();
        using var receiver = factory.CreateUserClient(receiverId);
        fault.Arm();

        var accepted = await receiver.PostAsJsonAsync("/api/team-invitations/accept", new
        {
            token = invitation.GetProperty("token").GetString()
        });

        Assert.True(fault.Injected, "The intended authorization interleaving must execute.");
        Assert.Contains(accepted.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.Conflict, HttpStatusCode.Gone });
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Null((await db.Set<DAL.Entities.TeamInvitation>().SingleAsync(value => value.Id == invitationId)).AcceptedAtUtc);
            Assert.Equal(receiverAlreadyInTeam ? 101 : (int?)null,
                (await db.Users.SingleAsync(user => user.Id == receiverId)).TeamId);
        });
    }

    [Fact]
    public async Task TeamRename_OwnerMembershipRevokedAtWrite_DoesNotChangeTeam()
    {
        var fault = new IssuerRevocationInterleaving(101, 101, RevocationPoint.TeamRename);
        using var factory = await DataIsolationFactory.CreateAsync(fault);
        await AssignOwnerAsync(factory);
        using var owner = factory.CreateUserClient(101);
        fault.Arm();

        var renamed = await owner.PutAsJsonAsync("/api/teams/101", new { name = "Unauthorized raced rename" });

        Assert.True(fault.Injected, "The intended authorization interleaving must execute.");
        Assert.Contains(renamed.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.Conflict });
        await factory.WithDatabaseAsync(async db =>
            Assert.Equal("Isolation team A", (await db.Teams.SingleAsync(team => team.Id == 101)).Name));
    }

    private static async Task AssignOwnerAsync(DataIsolationFactory factory) => await factory.WithDatabaseAsync(async db =>
    {
        (await db.Teams.SingleAsync(team => team.Id == 101)).OwnerId = 101;
        await db.SaveChangesAsync();
    });

    private enum RevocationPoint { ReceiverSnapshot, ReceiverJoined, TeamRename }

    private sealed class IssuerRevocationInterleaving(int issuerId, int receiverId, RevocationPoint point) : DbCommandInterceptor
    {
        private bool _armed;
        public bool Injected { get; private set; }
        public void Arm() => _armed = true;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (point == RevocationPoint.ReceiverSnapshot && command.CommandText.Contains("\"ConcurrencyStamp\"", StringComparison.Ordinal)
                && command.CommandText.Contains("\"NormalizedEmail\"", StringComparison.Ordinal) && ReferencesReceiver(command))
            {
                await RevokeAsync(command, cancellationToken);
            }
            return result;
        }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (point == RevocationPoint.ReceiverJoined && result == 1 &&
                command.CommandText.TrimStart().StartsWith("UPDATE \"AspNetUsers\"", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Split("WHERE", 2, StringSplitOptions.None)[0].Contains("\"TeamId\"", StringComparison.Ordinal) &&
                ReferencesReceiver(command))
            {
                await RevokeAsync(command, cancellationToken);
            }
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (point == RevocationPoint.TeamRename && IsTeamRename(command)) await RevokeAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (point == RevocationPoint.TeamRename && IsTeamRename(command)) await RevokeAsync(command, cancellationToken);
            return result;
        }

        private static bool IsTeamRename(DbCommand command) =>
            command.CommandText.TrimStart().StartsWith("UPDATE \"Teams\"", StringComparison.OrdinalIgnoreCase) &&
            command.CommandText.Split("WHERE", 2, StringSplitOptions.None)[0].Contains("\"Name\"", StringComparison.Ordinal);

        private bool ReferencesReceiver(DbCommand command) => command.Parameters.Cast<DbParameter>()
            .Any(parameter => parameter.Value is int id && id == receiverId || parameter.Value is long longId && longId == receiverId);

        private async Task RevokeAsync(DbCommand command, CancellationToken token)
        {
            if (!_armed || Injected) return;
            await using var revoke = command.Connection!.CreateCommand();
            revoke.Transaction = command.Transaction;
            revoke.CommandText = "UPDATE \"AspNetUsers\" SET \"TeamId\" = NULL, \"ConcurrencyStamp\" = @stamp WHERE \"Id\" = @issuer";
            var stamp = revoke.CreateParameter(); stamp.ParameterName = "@stamp"; stamp.Value = Guid.NewGuid().ToString(); revoke.Parameters.Add(stamp);
            var issuer = revoke.CreateParameter(); issuer.ParameterName = "@issuer"; issuer.Value = issuerId; revoke.Parameters.Add(issuer);
            Assert.Equal(1, await revoke.ExecuteNonQueryAsync(token));
            Injected = true;
        }
    }
}
