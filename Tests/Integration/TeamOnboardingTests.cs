using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using User = DAL.Entities.User;
using Team = DAL.Entities.Team;
using Xunit;

namespace Tests.Integration;

public class TeamOnboardingTests
{
    [Fact]
    public async Task ConfirmedUnteamedMember_CreatesOwnedTeamAndJoinsIt()
    {
        using var factory = await FixtureAsync();
        using var caller = factory.CreateUserClient(104);
        var response = await caller.PostAsJsonAsync("/api/teams", new { name = "My new team" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var team = await response.Content.ReadFromJsonAsync<JsonElement>();
        var teamId = team.GetProperty("id").GetInt32();
        Assert.Equal(104, team.GetProperty("ownerId").GetInt32());
        await factory.WithDatabaseAsync(async db => Assert.Equal(teamId, (await db.Users.SingleAsync(user => user.Id == 104)).TeamId));
        Assert.Equal(HttpStatusCode.OK, (await caller.GetAsync($"/api/teams/{teamId}")).StatusCode);
    }

    [Fact]
    public async Task CurrentTeamMember_CannotCreateSecondTeam()
    {
        using var factory = await FixtureAsync();
        using var caller = factory.CreateUserClient(101);
        Assert.Equal(HttpStatusCode.Conflict, (await caller.PostAsJsonAsync("/api/teams", new { name = "Second team" })).StatusCode);
    }

    [Fact]
    public async Task Creation_IgnoresSuppliedOwnerMembershipAndExistingTeamId()
    {
        using var factory = await FixtureAsync();
        using var caller = factory.CreateUserClient(104);
        var response = await caller.PostAsJsonAsync("/api/teams", new { id = 101, ownerId = 101, teamId = 101, name = "Safe ownership", users = new[] { new { id = 103 } }, roles = new[] { "admin" } });
        response.EnsureSuccessStatusCode();
        var team = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(101, team.GetProperty("id").GetInt32());
        Assert.Equal(104, team.GetProperty("ownerId").GetInt32());
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Equal(102, (await db.Users.SingleAsync(user => user.Id == 103)).TeamId);
            Assert.False(await db.UserRoles.AnyAsync(role => role.UserId == 104));
            Assert.Equal("Isolation team A", (await db.Teams.SingleAsync(value => value.Id == 101)).Name);
        });
    }

    [Theory]
    [InlineData(101, 101, HttpStatusCode.OK)]
    [InlineData(102, 101, HttpStatusCode.Forbidden)]
    [InlineData(101, 102, HttpStatusCode.NotFound)]
    public async Task Rename_RequiresOwnerOfCurrentTeam(int callerId, int teamId, HttpStatusCode expected)
    {
        using var factory = await FixtureAsync();
        using var caller = factory.CreateUserClient(callerId);
        var response = await caller.PutAsJsonAsync($"/api/teams/{teamId}", new { name = "Owned rename", ownerId = 102 });
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
            await factory.WithDatabaseAsync(async db => Assert.Equal(101, db.Entry(await db.Teams.SingleAsync(value => value.Id == 101)).Property("OwnerId").CurrentValue));
    }

    [Fact]
    public async Task OwnerInvitation_StoresHashAndAnonymousPreviewExposesNoDirectory()
    {
        using var factory = await FixtureAsync();
        using var owner = factory.CreateUserClient(101);
        var invitation = await InviteAsync(owner, 101, "member106@example.test");
        await factory.WithDatabaseAsync(async db =>
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT TokenHash FROM TeamInvitations";
            var hash = Assert.IsType<string>(await command.ExecuteScalarAsync());
            Assert.NotEqual(invitation.Token, hash);
            Assert.Equal(64, hash.Length);
        });
        using var anonymous = factory.CreateUserClient();
        var response = await anonymous.GetAsync($"/api/team-invitations/preview?token={Uri.EscapeDataString(invitation.Token)}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        Assert.Equal("Isolation team A", json.RootElement.GetProperty("teamName").GetString());
        Assert.DoesNotContain("@example.test", body);
        Assert.DoesNotContain("tokenHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("users", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(102, 101, HttpStatusCode.Forbidden)]
    [InlineData(101, 102, HttpStatusCode.NotFound)]
    public async Task Invitation_CannotBeIssuedByMemberOrForeignOwner(int callerId, int teamId, HttpStatusCode expected)
    {
        using var factory = await FixtureAsync();
        using var caller = factory.CreateUserClient(callerId);
        var response = await caller.PostAsJsonAsync($"/api/teams/{teamId}/invitations", new { email = "member106@example.test" });
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task MatchingVerifiedInvitee_AcceptsAndReplayDoesNotChangeRolesOrMembership()
    {
        using var factory = await FixtureAsync();
        using var owner = factory.CreateUserClient(101);
        var invitation = await InviteAsync(owner, 101, "member106@example.test");
        using var invitee = factory.CreateUserClient(106);
        var payload = new { token = invitation.Token, teamId = 102, roles = new[] { "admin" } };
        Assert.Equal(HttpStatusCode.OK, (await invitee.PostAsJsonAsync("/api/team-invitations/accept", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await invitee.PostAsJsonAsync("/api/team-invitations/accept", payload)).StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Equal(101, (await db.Users.SingleAsync(user => user.Id == 106)).TeamId);
            Assert.False(await db.UserRoles.AnyAsync(role => role.UserId == 106));
        });
    }

    [Fact]
    public async Task Invitation_IsBoundToVerifiedEmailOfAcceptingUser()
    {
        using var factory = await FixtureAsync();
        using var owner = factory.CreateUserClient(101);
        var invitation = await InviteAsync(owner, 101, "member106@example.test");
        using var wrongUser = factory.CreateUserClient(104);
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongUser.PostAsJsonAsync("/api/team-invitations/accept", new { token = invitation.Token })).StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.Null((await db.Users.SingleAsync(user => user.Id == 104)).TeamId));
        await factory.WithDatabaseAsync(async db =>
        {
            (await db.Users.SingleAsync(user => user.Id == 106)).EmailConfirmed = false;
            await db.SaveChangesAsync();
        });
        using var unconfirmed = factory.CreateUserClient(106);
        Assert.Equal(HttpStatusCode.Forbidden, (await unconfirmed.PostAsJsonAsync("/api/team-invitations/accept", new { token = invitation.Token })).StatusCode);
    }

    [Fact]
    public async Task Invitation_CannotMoveExistingMemberIntoForeignTeam()
    {
        using var factory = await FixtureAsync();
        using var owner = factory.CreateUserClient(103);
        var invitation = await InviteAsync(owner, 102, "member101@example.test");
        using var invitee = factory.CreateUserClient(101);
        Assert.Equal(HttpStatusCode.Conflict, (await invitee.PostAsJsonAsync("/api/team-invitations/accept", new { token = invitation.Token })).StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.Equal(101, (await db.Users.SingleAsync(user => user.Id == 101)).TeamId));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    public async Task ExpiredOrRevokedInvitation_CannotAddMember(string state)
    {
        using var factory = await FixtureAsync();
        using var owner = factory.CreateUserClient(101);
        var invitation = await InviteAsync(owner, 101, "member106@example.test");
        if (state == "expired")
            await factory.WithDatabaseAsync(async db => { await db.Database.ExecuteSqlRawAsync("UPDATE TeamInvitations SET ExpiresAtUtc = '2000-01-01 00:00:00'"); });
        else
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/teams/101/invitations/{invitation.Id}")).StatusCode);
        using var invitee = factory.CreateUserClient(106);
        Assert.Equal(HttpStatusCode.Gone, (await invitee.PostAsJsonAsync("/api/team-invitations/accept", new { token = invitation.Token })).StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.Null((await db.Users.SingleAsync(user => user.Id == 106)).TeamId));
    }

    [Fact]
    public async Task OwnerRemoval_ImmediatelyRevokesMembersCurrentTeamAccess()
    {
        using var factory = await FixtureAsync();
        using var owner = factory.CreateUserClient(101);
        using var member = factory.CreateUserClient(102);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/projects/101")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/teams/101/members/102")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/api/projects/101")).StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.Null((await db.Users.SingleAsync(user => user.Id == 102)).TeamId));
    }

    [Theory]
    [InlineData(102, 101, HttpStatusCode.Forbidden)]
    [InlineData(101, 101, HttpStatusCode.Conflict)]
    [InlineData(101, 103, HttpStatusCode.NotFound)]
    public async Task Removal_RejectsMemberManagementOwnerOrForeignMember(int callerId, int targetId, HttpStatusCode expected)
    {
        using var factory = await FixtureAsync();
        using var caller = factory.CreateUserClient(callerId);
        Assert.Equal(expected, (await caller.DeleteAsync($"/api/teams/101/members/{targetId}")).StatusCode);
    }

    [Theory]
    [InlineData(102, HttpStatusCode.NoContent)]
    [InlineData(101, HttpStatusCode.Conflict)]
    public async Task MemberCanLeaveButOwnerMustDisbandOrTransferFirst(int callerId, HttpStatusCode expected)
    {
        using var factory = await FixtureAsync();
        using var caller = factory.CreateUserClient(callerId);
        Assert.Equal(expected, (await caller.PostAsync("/api/teams/leave", null)).StatusCode);
        if (expected == HttpStatusCode.NoContent)
            await factory.WithDatabaseAsync(async db => Assert.Null((await db.Users.SingleAsync(user => user.Id == callerId)).TeamId));
    }

    [Fact]
    public async Task OwnerDeletingEmptyTeam_ReleasesMembersWithoutDeletingAccounts()
    {
        using var factory = await FixtureAsync();
        using var owner = factory.CreateUserClient(104);
        var created = await owner.PostAsJsonAsync("/api/teams", new { name = "Disposable owned team" });
        created.EnsureSuccessStatusCode();
        var teamId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var invitation = await InviteAsync(owner, teamId, "member106@example.test");
        using var invitee = factory.CreateUserClient(106);
        Assert.Equal(HttpStatusCode.OK, (await invitee.PostAsJsonAsync("/api/team-invitations/accept", new { token = invitation.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/teams/{teamId}")).StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.False(await db.Teams.AnyAsync(team => team.Id == teamId));
            Assert.Null((await db.Users.SingleAsync(user => user.Id == 104)).TeamId);
            Assert.Null((await db.Users.SingleAsync(user => user.Id == 106)).TeamId);
        });
    }

    [Fact]
    public async Task OwnerLosingCurrentMembership_CannotManageFormerOwnedTeam()
    {
        using var factory = await FixtureAsync();
        await factory.WithDatabaseAsync(async db =>
        {
            (await db.Users.SingleAsync(user => user.Id == 101)).TeamId = null;
            await db.SaveChangesAsync();
        });
        using var formerOwner = factory.CreateUserClient(101);
        Assert.Equal(HttpStatusCode.NotFound, (await formerOwner.PutAsJsonAsync("/api/teams/101", new { name = "Unauthorized" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await formerOwner.PostAsJsonAsync("/api/teams/101/invitations", new { email = "member106@example.test" })).StatusCode);
    }

    private static async Task<DataIsolationFactory> FixtureAsync()
    {
        var factory = await DataIsolationFactory.CreateAsync();
        await factory.WithDatabaseAsync(async db =>
        {
            db.Users.Add(new User
            {
                Id = 106, UserName = "member106", NormalizedUserName = "MEMBER106", Email = "member106@example.test",
                NormalizedEmail = "MEMBER106@EXAMPLE.TEST", FirstName = "Invited", LastName = "Member", EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            });
            // Metadata access keeps this runtime reproducer compilable before
            // the additive owner property exists; current routes still execute.
            if (db.Model.FindEntityType(typeof(Team))?.FindProperty("OwnerId") is not null)
            {
                db.Entry(await db.Teams.SingleAsync(team => team.Id == 101)).Property("OwnerId").CurrentValue = 101;
                db.Entry(await db.Teams.SingleAsync(team => team.Id == 102)).Property("OwnerId").CurrentValue = 103;
            }
            await db.SaveChangesAsync();
        });
        return factory;
    }

    private static async Task<InviteToken> InviteAsync(HttpClient owner, int teamId, string email)
    {
        var response = await owner.PostAsJsonAsync($"/api/teams/{teamId}/invitations", new { email });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invitation = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new InviteToken(invitation.GetProperty("id").GetGuid(), invitation.GetProperty("token").GetString()!);
    }

    private sealed record InviteToken(Guid Id, string Token);
}
