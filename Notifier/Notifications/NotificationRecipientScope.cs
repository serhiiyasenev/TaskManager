using DAL.Context;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Notifier.Notifications;

public interface INotificationRecipientScope
{
    Task<string?> GetGroupAsync(int userId, CancellationToken ct = default, string? expectedSecurityStamp = null);
}

public sealed class DatabaseNotificationRecipientScope(TaskContext db, TimeProvider clock) : INotificationRecipientScope
{
    public async Task<string?> GetGroupAsync(int userId, CancellationToken ct = default, string? expectedSecurityStamp = null)
    {
        if (userId <= 0) return null;
        var user = await db.Users.AsNoTracking().Where(value => value.Id == userId)
            .Select(value => new { value.TeamId, value.SecurityStamp, value.EmailConfirmed, value.LockoutEnd })
            .SingleOrDefaultAsync(ct);
        if (user is null || !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.SecurityStamp)
            || user.LockoutEnd > clock.GetUtcNow()
            || (expectedSecurityStamp is not null && expectedSecurityStamp != user.SecurityStamp)) return null;
        var roles = await (from membership in db.UserRoles.AsNoTracking()
                           join role in db.Roles.AsNoTracking() on membership.RoleId equals role.Id
                           where membership.UserId == userId
                           orderby role.NormalizedName
                           select role.NormalizedName).ToArrayAsync(ct);
        // Both connection and sender resolve this from current SQL. A socket
        // joined before stamp/team/role revocation remains in the old group.
        var scope = JsonSerializer.Serialize(new { userId, user.TeamId, user.SecurityStamp, roles });
        return "inbox:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
    }
}
