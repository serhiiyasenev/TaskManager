using BLL.Interfaces;
using DAL.Context;
using Microsoft.Extensions.Logging;

namespace BLL.Services.Notifications;

// Temporary explicit TDD checkpoint. Implement only after behavioral RED.
public sealed class NotificationMessageProcessor : INotificationMessageProcessor
{
    public NotificationMessageProcessor(TaskContext db, TimeProvider clock, ILogger<NotificationMessageProcessor> logger) { }
    public Task<NotificationProcessingResult> ProcessAsync(Guid messageId, string immutablePayload, CancellationToken ct = default)
        => throw new NotImplementedException();
}
