using BLL.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Notifier.Notifications;

public sealed class InboxRealtimePublisher(IServiceScopeFactory scopeFactory, IHubContext<ChatHub> hub)
    : IInboxRealtimePublisher
{
    public Task PublishAsync(Guid notificationId, int recipientId, CancellationToken ct = default) => throw new NotImplementedException();
}
