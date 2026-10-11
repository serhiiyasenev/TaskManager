using DAL.Context;

namespace Notifier.Notifications;

public interface INotificationRecipientScope
{
    Task<string?> GetGroupAsync(int userId, CancellationToken ct = default);
}

public sealed class DatabaseNotificationRecipientScope(TaskContext db, TimeProvider clock) : INotificationRecipientScope
{
    public Task<string?> GetGroupAsync(int userId, CancellationToken ct = default) => throw new NotImplementedException();
}
