using BLL.Authorization;
using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Notifications;
using DAL.Context;
using Microsoft.Extensions.Logging;

namespace BLL.Services.Notifications;

// Temporary explicit TDD checkpoint. Implement only after behavioral RED.
public sealed class NotificationInboxService : INotificationInboxService
{
    public NotificationInboxService(TaskContext db, IUserAccess access, TimeProvider clock, ILogger<NotificationInboxService> logger) { }
    public Task<Result<NotificationInboxPageDto>> GetPageAsync(int page = 1, int pageSize = 20,
        bool unreadOnly = false, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Result<NotificationInboxItemDto>> MarkReadAsync(Guid notificationId, CancellationToken ct = default)
        => throw new NotImplementedException();
}
