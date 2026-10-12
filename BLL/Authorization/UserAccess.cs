#nullable enable annotations
using System.Security.Claims;
using DAL.Context;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BLL.Authorization;

public interface ICurrentUserContext
{
    int? UserId { get; }
    bool IsAdmin { get; }
}

public sealed class HttpCurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    public int? UserId => accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
        && int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;

    public bool IsAdmin => UserId.HasValue && accessor.HttpContext!.User.IsInRole("admin");
}

public sealed record UserAccessScope(int UserId, int? TeamId, bool IsAdmin);

public interface IUserAccess
{
    Task<UserAccessScope?> GetScopeAsync(CancellationToken ct = default);
}

public sealed class DatabaseUserAccess(TaskContext db, ICurrentUserContext caller) : IUserAccess
{
    public async Task<UserAccessScope?> GetScopeAsync(CancellationToken ct = default)
    {
        if (caller.UserId is not { } userId) return null;
        // Membership and roles are read from the database on every operation:
        // stale cookies/JWT claims must not preserve revoked access.
        var user = await db.Users.AsNoTracking().Where(user => user.Id == userId)
            .Select(user => new { user.TeamId }).SingleOrDefaultAsync(ct);
        if (user is null) return null;
        var isAdmin = caller.IsAdmin && await (
            from membership in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on membership.RoleId equals role.Id
            where membership.UserId == userId && role.NormalizedName == "ADMIN"
            select membership.UserId).AnyAsync(ct);
        return new UserAccessScope(userId, user.TeamId, isAdmin);
    }
}

internal static class AccessQueries
{
    public static IQueryable<Project> VisibleProjects(this IQueryable<Project> query, UserAccessScope? scope) =>
        scope is null || scope.IsAdmin ? query : query.Where(project => project.TeamId == scope.TeamId);

    public static IQueryable<DAL.Entities.Task> VisibleTasks(this IQueryable<DAL.Entities.Task> query, UserAccessScope? scope) =>
        scope is null || scope.IsAdmin ? query : query.Where(task => task.Project.TeamId == scope.TeamId);

    public static IQueryable<Team> VisibleTeams(this IQueryable<Team> query, UserAccessScope? scope) =>
        scope is null || scope.IsAdmin ? query : query.Where(team => team.Id == scope.TeamId);

    public static IQueryable<User> VisibleUsers(this IQueryable<User> query, UserAccessScope? scope) =>
        scope is null || scope.IsAdmin ? query : query.Where(user => user.Id == scope.UserId || (scope.TeamId.HasValue && user.TeamId == scope.TeamId));

    public static async System.Threading.Tasks.Task RequireAdministratorAsync(IUserAccess? access)
    {
        // The null path supports existing direct service tests. Production
        // registration must explicitly resolve the required access service.
        if (access is not null && await access.GetScopeAsync() is not { IsAdmin: true })
            throw new UnauthorizedAccessException("Administrator access is required.");
    }
}
