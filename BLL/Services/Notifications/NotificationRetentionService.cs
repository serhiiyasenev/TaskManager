using BLL.Interfaces;
using DAL.Context;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services.Notifications;

public sealed class NotificationRetentionService(TaskContext db, TimeProvider clock) : INotificationRetentionService
{
    public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-90);
        var expired = await db.UserNotifications.Where(value => value.OccurredAtUtc <= cutoff)
            .OrderBy(value => value.OccurredAtUtc).ThenBy(value => value.Id).Take(100).ToListAsync(ct);
        db.UserNotifications.RemoveRange(expired);
        await db.SaveChangesAsync(ct);
        // Processing markers have no private names. Keeping them prevents a
        // delayed DLQ replay from recreating already erased inbox content.
        return expired.Count;
    }
}
