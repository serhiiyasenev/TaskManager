#nullable enable
using BLL.Common;
using BLL.Models.Notifications;

namespace BLL.Interfaces;

public interface INotificationInboxService
{
    Task<Result<NotificationInboxPageDto>> GetPageAsync(int page = 1, int pageSize = 20,
        bool unreadOnly = false, CancellationToken ct = default);
    Task<Result<NotificationInboxItemDto>> MarkReadAsync(Guid notificationId, CancellationToken ct = default);
}

public interface INotificationMessageProcessor
{
    // Return only after notification/marker commit. InvalidDataException is a
    // permanent invalid envelope; other failures leave settlement to transport.
    Task<NotificationProcessingResult> ProcessAsync(Guid messageId, string immutablePayload, CancellationToken ct = default);
}

public sealed record NotificationProcessingResult(Guid MessageId, int? RecipientId,
    bool Created, bool Suppressed);

public interface IInboxRealtimePublisher
{
    // A hint is optional after durable commit; routing must use current scope.
    Task PublishAsync(Guid notificationId, int recipientId, CancellationToken ct = default);
}

public interface ITaskNotificationDispatcher
{
    // Shared lease/confirm implementation: worker and legacy immediate request
    // use the same claim. True means broker-confirmed; never merely SQL-queued.
    Task<bool> TryDispatchAsync(Guid messageId, CancellationToken ct = default);
}

public interface IAiDraftRetentionService
{
    Task<int> CleanupExpiredAsync(CancellationToken ct = default);
}

public interface INotificationRetentionService
{
    // Erase old inbox content in bounded batches; deduplication markers survive.
    Task<int> CleanupExpiredAsync(CancellationToken ct = default);
}
