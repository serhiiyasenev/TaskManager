using BLL.Interfaces;
using Microsoft.AspNetCore.SignalR;
using BLL.Models.Notifications;
using DAL.Context;
using Microsoft.EntityFrameworkCore;

namespace Notifier.Notifications;

public sealed class InboxRealtimePublisher(IServiceScopeFactory scopeFactory, IHubContext<ChatHub> hub)
    : IInboxRealtimePublisher
{
    public async Task PublishAsync(Guid notificationId, int recipientId, CancellationToken ct = default)
    {
        if (notificationId == Guid.Empty || recipientId <= 0) throw new ArgumentException("A notification identity is required.");
        await using var scope = scopeFactory.CreateAsyncScope();
        var recipients = scope.ServiceProvider.GetRequiredService<INotificationRecipientScope>();
        var group = await recipients.GetGroupAsync(recipientId, ct);
        if (group is null) return;
        var db = scope.ServiceProvider.GetRequiredService<TaskContext>();
        var visible = await (from notification in db.UserNotifications.AsNoTracking()
            join task in db.Tasks.AsNoTracking() on notification.TaskId equals task.Id
            join project in db.Projects.AsNoTracking() on task.ProjectId equals project.Id
            join owner in db.Users.AsNoTracking() on notification.OwnerId equals owner.Id
            where notification.Id == notificationId && notification.OwnerId == recipientId && task.PerformerId == recipientId
                && notification.ProjectId == task.ProjectId && owner.TeamId == project.TeamId
            select notification.Id).AnyAsync(ct);
        if (!visible || group != await recipients.GetGroupAsync(recipientId, ct)) return;
        var target = hub.Clients.Group(group);
        await target.SendAsync("InboxChanged", new NotificationHintDto(notificationId), ct);
        // Existing console clients retain their event signature. Private task
        // content is obtained through the scoped API instead of the socket.
        await target.SendAsync("ReceiveMessage", "TaskManager", "Your notification inbox changed.", ct);
    }
}
