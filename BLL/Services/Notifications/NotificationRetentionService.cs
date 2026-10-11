using BLL.Interfaces;
using DAL.Context;

namespace BLL.Services.Notifications;

// Temporary explicit TDD checkpoint. Content expires; dedup markers survive.
public sealed class NotificationRetentionService : INotificationRetentionService
{
    public NotificationRetentionService(TaskContext db, TimeProvider clock) { }
    public Task<int> CleanupExpiredAsync(CancellationToken ct = default) => throw new NotImplementedException();
}
