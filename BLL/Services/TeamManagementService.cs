using System.Security.Cryptography;
using System.Text;
using BLL.Authorization;
using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Teams;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public sealed class TeamManagementService(TaskContext db, IUserAccess access, TimeProvider clock, ILogger<TeamManagementService> logger)
    : ITeamManagementService
{
    private static readonly Error Gone = Error.Custom("Error.Gone", "This invitation is no longer available.");
    private DbSet<TeamInvitation> Invitations => db.Set<TeamInvitation>();
    private IQueryable<int> AdministratorIds => from membership in db.UserRoles
        join role in db.Roles on membership.RoleId equals role.Id
        where role.NormalizedName == "ADMIN"
        select membership.UserId;

    public async Task<Result<Team>> CreateTeamAsync(string name, CancellationToken ct = default)
    {
        var caller = await access.GetScopeAsync(ct);
        if (caller is null) return Error.Unauthorized;
        return await InTransactionAsync<Team>(async () =>
        {
            caller = await access.GetScopeAsync(ct);
            if (caller is null) return Error.Unauthorized;
            var user = await ReadMemberAsync(caller.UserId, ct);
            if (user is null) return Error.Unauthorized;
            if (!user.EmailConfirmed) return Error.Forbidden;
            if (user.TeamId.HasValue && !caller.IsAdmin) return Error.Conflict("Leave your current team before creating another team.");
            if (user.TeamId.HasValue && await LockAdministratorAsync(user, ct) != 1)
                return Error.Conflict("Your administrator access changed; refresh and retry.");
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) return Error.Validation("Name", "A team name of at most 100 characters is required.");
            name = name.Trim();
            if (await db.Teams.AnyAsync(team => team.Name == name, ct)) return Error.Conflict("A team with this name already exists.");
            var team = new Team { Name = name, OwnerId = user.Id, CreatedAt = clock.GetUtcNow().UtcDateTime };
            db.Teams.Add(team);
            await db.SaveChangesAsync(ct);
            if (user.TeamId is null && await ChangeMembershipAsync(user, team.Id, ct, requireConfirmed: true) != 1)
                return Error.Conflict("Your membership changed; refresh and retry.");
            return Result<Team>.Success(team);
        }, ct);
    }

    public async Task<Result> DeleteTeamAsync(int teamId, CancellationToken ct = default)
    {
        var allowed = await ManageTeamAsync(teamId, ct);
        if (allowed.IsFailure) return allowed.Error;
        return await InTransactionAsync(async () =>
        {
            allowed = await ManageTeamAsync(teamId, ct);
            if (allowed.IsFailure) return allowed.Error;
            if (!await LockManagerAsync(teamId, ct)) return Error.Conflict("Your team access changed; refresh and retry.");
            if (await db.Projects.AnyAsync(project => project.TeamId == teamId, ct))
                return Error.Conflict("Delete the team's projects before disbanding the team.");
            var stamp = Guid.NewGuid().ToString();
            await db.Users.Where(user => user.TeamId == teamId).ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.TeamId, (int?)null)
                .SetProperty(user => user.ConcurrencyStamp, stamp), ct);
            db.Teams.Remove(allowed.Value!);
            return Result.Success();
        }, ct);
    }

    public async Task<Result<CreatedTeamInvitation>> CreateInvitationAsync(int teamId, string email, CancellationToken ct = default)
    {
        var allowed = await ManageTeamAsync(teamId, ct);
        if (allowed.IsFailure) return allowed.Error;
        return await InTransactionAsync<CreatedTeamInvitation>(async () =>
        {
            allowed = await ManageTeamAsync(teamId, ct);
            if (allowed.IsFailure) return allowed.Error;
            if (!await LockManagerAsync(teamId, ct)) return Error.Conflict("Your team access changed; refresh and retry.");
            var caller = await access.GetScopeAsync(ct);
            if (caller is null) return Error.Unauthorized;
            email = email?.Trim() ?? "";
            if (email.Length is < 3 or > 256 || !System.Net.Mail.MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
                return Error.Validation("Email", "A valid email address is required.");
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var now = clock.GetUtcNow().UtcDateTime;
            var invitation = new TeamInvitation
            {
                Id = Guid.NewGuid(), TeamId = teamId, IssuedByUserId = caller.UserId, Email = email,
                NormalizedEmail = NormalizeEmail(email), TokenHash = Hash(token), CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(7), Version = Guid.NewGuid()
            };
            Invitations.Add(invitation);
            return Result<CreatedTeamInvitation>.Success(new(invitation.Id, invitation.Email, token, invitation.ExpiresAtUtc));
        }, ct);
    }

    public async Task<Result<List<TeamInvitationDetail>>> GetInvitationsAsync(int teamId, CancellationToken ct = default)
    {
        var allowed = await ManageTeamAsync(teamId, ct);
        if (allowed.IsFailure) return allowed.Error;
        return Result<List<TeamInvitationDetail>>.Success(await Invitations.AsNoTracking().Where(value => value.TeamId == teamId)
            .OrderByDescending(value => value.CreatedAtUtc).Select(value => new TeamInvitationDetail(value.Id, value.Email,
                value.CreatedAtUtc, value.ExpiresAtUtc, value.AcceptedAtUtc, value.RevokedAtUtc)).ToListAsync(ct));
    }

    public async Task<Result> RevokeInvitationAsync(int teamId, Guid invitationId, CancellationToken ct = default)
    {
        var allowed = await ManageTeamAsync(teamId, ct);
        if (allowed.IsFailure) return allowed.Error;
        return await InTransactionAsync(async () =>
        {
            allowed = await ManageTeamAsync(teamId, ct);
            if (allowed.IsFailure) return allowed.Error;
            if (!await LockManagerAsync(teamId, ct)) return Error.Conflict("Your team access changed; refresh and retry.");
            var invitation = await Invitations.SingleOrDefaultAsync(value => value.Id == invitationId && value.TeamId == teamId, ct);
            if (invitation is null) return Error.NotFound("Invitation", invitationId.ToString());
            if (invitation.AcceptedAtUtc.HasValue) return Error.Conflict("This invitation has already been accepted.");
            invitation.RevokedAtUtc = clock.GetUtcNow().UtcDateTime;
            invitation.Version = Guid.NewGuid();
            return Result.Success();
        }, ct);
    }

    public async Task<Result<TeamInvitationPreview>> PreviewInvitationAsync(string token, CancellationToken ct = default)
    {
        if (!ValidToken(token)) return Error.Validation("Token", "The invitation token is invalid.");
        var hash = Hash(token);
        var invitation = await Invitations.AsNoTracking().SingleOrDefaultAsync(value => value.TokenHash == hash, ct);
        if (invitation is null) return Error.NotFound("Invitation", "unavailable");
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(value => value.Id == invitation.TeamId, ct);
        if (team is null) return Error.NotFound("Invitation", "unavailable");
        var expired = invitation.ExpiresAtUtc <= clock.GetUtcNow().UtcDateTime || invitation.RevokedAtUtc.HasValue
            || invitation.AcceptedAtUtc.HasValue || !await IssuerCanManageAsync(invitation, ct);
        return Result<TeamInvitationPreview>.Success(new(team.Name, expired));
    }

    public async Task<Result<AcceptedTeamInvitation>> AcceptInvitationAsync(string token, CancellationToken ct = default)
    {
        var caller = await access.GetScopeAsync(ct);
        if (caller is null) return Error.Unauthorized;
        if (!ValidToken(token)) return Error.Validation("Token", "The invitation token is invalid.");
        var hash = Hash(token);
        return await InTransactionAsync<AcceptedTeamInvitation>(async () =>
        {
            caller = await access.GetScopeAsync(ct);
            if (caller is null) return Error.Unauthorized;
            var invitation = await Invitations.SingleOrDefaultAsync(value => value.TokenHash == hash, ct);
            if (invitation is null) return Error.NotFound("Invitation", "unavailable");
            var now = clock.GetUtcNow().UtcDateTime;
            if (invitation.RevokedAtUtc.HasValue || invitation.ExpiresAtUtc <= now || !await IssuerCanManageAsync(invitation, ct)) return Gone;
            var user = await ReadMemberAsync(caller.UserId, ct);
            if (user is null) return Error.Unauthorized;
            if (!user.EmailConfirmed || user.NormalizedEmail != invitation.NormalizedEmail) return Error.Forbidden;
            if (invitation.AcceptedAtUtc.HasValue)
                return invitation.AcceptedByUserId == user.Id && user.TeamId == invitation.TeamId
                    ? Result<AcceptedTeamInvitation>.Success(new(invitation.TeamId)) : Gone;
            if (user.TeamId.HasValue && user.TeamId != invitation.TeamId)
                return Error.Conflict("Leave your current team before accepting this invitation.");
            if (user.TeamId is null)
            {
                // The SQL update repeats issuer rights and invitation validity at
                // the membership write, and compares the receiver's current stamp.
                var stamp = Guid.NewGuid().ToString();
                var authorizedIssuer = AuthorizedIssuerQuery(invitation);
                var updated = await db.Users.Where(member => member.Id == user.Id && member.TeamId == null
                    && member.ConcurrencyStamp == user.ConcurrencyStamp && member.EmailConfirmed
                    && member.NormalizedEmail == invitation.NormalizedEmail && authorizedIssuer.Any()
                    && Invitations.Any(value => value.Id == invitation.Id && value.Version == invitation.Version
                        && value.RevokedAtUtc == null && value.AcceptedAtUtc == null && value.ExpiresAtUtc > now))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(member => member.TeamId, (int?)invitation.TeamId)
                        .SetProperty(member => member.ConcurrencyStamp, stamp), ct);
                if (updated != 1) return Error.Conflict("Your membership or invitation changed; refresh and retry.");
            }
            invitation.AcceptedAtUtc = now;
            invitation.AcceptedByUserId = user.Id;
            invitation.Version = Guid.NewGuid();
            return Result<AcceptedTeamInvitation>.Success(new(invitation.TeamId));
        }, ct);
    }

    public async Task<Result> RemoveMemberAsync(int teamId, int userId, CancellationToken ct = default)
    {
        var allowed = await ManageTeamAsync(teamId, ct);
        if (allowed.IsFailure) return allowed.Error;
        return await InTransactionAsync(async () =>
        {
            allowed = await ManageTeamAsync(teamId, ct);
            if (allowed.IsFailure) return allowed.Error;
            if (!await LockManagerAsync(teamId, ct)) return Error.Conflict("Your team access changed; refresh and retry.");
            if (allowed.Value!.OwnerId == userId) return Error.Conflict("The owner must disband the team before leaving it.");
            var user = await ReadMemberAsync(userId, ct);
            if (user is null || user.TeamId != teamId) return Error.NotFound("Member", userId);
            if (await ChangeMembershipAsync(user, null, ct) != 1) return Error.Conflict("The membership changed; refresh and retry.");
            return Result.Success();
        }, ct);
    }

    public async Task<Result> LeaveTeamAsync(CancellationToken ct = default)
    {
        var caller = await access.GetScopeAsync(ct);
        if (caller is null) return Error.Unauthorized;
        return await InTransactionAsync(async () =>
        {
            caller = await access.GetScopeAsync(ct);
            if (caller is null) return Error.Unauthorized;
            var user = await ReadMemberAsync(caller.UserId, ct);
            if (user is null) return Error.Unauthorized;
            if (user.TeamId is null) return Result.Success();
            var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(value => value.Id == user.TeamId, ct);
            if (team?.OwnerId == user.Id) return Error.Conflict("The owner must disband the team before leaving it.");
            if (await ChangeMembershipAsync(user, null, ct) != 1) return Error.Conflict("Your membership changed; refresh and retry.");
            return Result.Success();
        }, ct);
    }

    private async Task<Result<Team>> ManageTeamAsync(int teamId, CancellationToken ct)
    {
        var caller = await access.GetScopeAsync(ct);
        if (caller is null) return Error.Unauthorized;
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(value => value.Id == teamId, ct);
        if (team is null || (!caller.IsAdmin && caller.TeamId != teamId)) return Error.NotFound("Team", teamId);
        if (!caller.IsAdmin && team.OwnerId != caller.UserId) return Error.Forbidden;
        return Result<Team>.Success(team);
    }

    private IQueryable<Team> AuthorizedIssuerQuery(TeamInvitation invitation) => db.Teams.Where(team => team.Id == invitation.TeamId
        && db.Users.Any(user => user.Id == invitation.IssuedByUserId && user.EmailConfirmed)
        && ((team.OwnerId == invitation.IssuedByUserId && db.Users.Any(user => user.Id == invitation.IssuedByUserId
            && user.TeamId == team.Id)) || AdministratorIds.Contains(invitation.IssuedByUserId)));

    private Task<bool> IssuerCanManageAsync(TeamInvitation invitation, CancellationToken ct) => AuthorizedIssuerQuery(invitation).AnyAsync(ct);

    private Task<MemberSnapshot?> ReadMemberAsync(int id, CancellationToken ct) => db.Users.AsNoTracking().Where(user => user.Id == id)
        .Select(user => new MemberSnapshot(user.Id, user.TeamId, user.ConcurrencyStamp, user.EmailConfirmed, user.NormalizedEmail)).SingleOrDefaultAsync(ct);

    private Task<int> ChangeMembershipAsync(MemberSnapshot user, int? teamId, CancellationToken ct, bool requireConfirmed = false)
    {
        var stamp = Guid.NewGuid().ToString();
        return db.Users.Where(value => value.Id == user.Id && value.TeamId == user.TeamId
            && value.ConcurrencyStamp == user.ConcurrencyStamp && (!requireConfirmed || value.EmailConfirmed))
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.TeamId, teamId)
                .SetProperty(value => value.ConcurrencyStamp, stamp), ct);
    }

    private Task<int> LockAdministratorAsync(MemberSnapshot user, CancellationToken ct)
    {
        var stamp = Guid.NewGuid().ToString();
        return db.Users.Where(value => value.Id == user.Id && value.TeamId == user.TeamId
            && value.ConcurrencyStamp == user.ConcurrencyStamp && value.EmailConfirmed && AdministratorIds.Contains(user.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ConcurrencyStamp, stamp), ct);
    }

    private async Task<bool> LockManagerAsync(int teamId, CancellationToken ct)
    {
        var caller = await access.GetScopeAsync(ct);
        if (caller is null) return false;
        var user = await ReadMemberAsync(caller.UserId, ct);
        if (user is null) return false;
        var stamp = Guid.NewGuid().ToString();
        // This compare-and-swap repeats current rights at the SQL write and
        // holds the actor's membership row for the remaining transaction.
        return await db.Users.Where(value => value.Id == user.Id && value.TeamId == user.TeamId
            && value.ConcurrencyStamp == user.ConcurrencyStamp && value.EmailConfirmed
            && ((caller.IsAdmin && AdministratorIds.Contains(user.Id))
                || (value.TeamId == teamId && db.Teams.Any(team => team.Id == teamId && team.OwnerId == value.Id))))
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ConcurrencyStamp, stamp), ct) == 1;
    }

    private async Task<Result<T>> InTransactionAsync<T>(Func<Task<Result<T>>> action, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await action();
            if (result.IsFailure)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                return result;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            logger.LogWarning("A team membership command conflicted with another update.");
            return Error.Conflict("The team or membership changed; refresh and retry.");
        }
        catch (Exception exception)
        {
            db.ChangeTracker.Clear();
            logger.LogError("A team membership command failed ({ErrorType}).", exception.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    private async Task<Result> InTransactionAsync(Func<Task<Result>> action, CancellationToken ct)
    {
        var result = await InTransactionAsync<int>(async () =>
        {
            var operation = await action();
            return operation.IsSuccess ? Result<int>.Success(1) : Result<int>.Failure(operation.Error);
        }, ct);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    private static bool ValidToken(string? token) => token is { Length: >= 32 and <= 128 }
        && token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private static string NormalizeEmail(string email) => email.Trim().Normalize().ToUpperInvariant();
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private sealed record MemberSnapshot(int Id, int? TeamId, string? ConcurrencyStamp, bool EmailConfirmed, string? NormalizedEmail);
}
