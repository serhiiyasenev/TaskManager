#nullable enable
using BLL.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BLL.Services.Notifications;

public sealed record NotificationDelivery(string? MessageId, ReadOnlyMemory<byte> Body, int Attempt = 1);

public interface INotificationDeliverySettlement
{
    Task CompleteAsync(CancellationToken ct);
    Task RetryAsync(int nextAttempt, CancellationToken ct);
    Task DeadLetterAsync(string safeReason, CancellationToken ct);
}

public sealed class NotificationDeliveryCoordinator(
    IServiceScopeFactory scopeFactory,
    IInboxRealtimePublisher realtime,
    ILogger<NotificationDeliveryCoordinator> logger)
{
    public Task ProcessAsync(NotificationDelivery delivery, INotificationDeliverySettlement settlement,
        CancellationToken ct = default) => throw new NotImplementedException();
}

public static class NotificationDeliveryMetadata
{
    public const string RetryCountHeader = "x-taskmanager-retry-count";
    public static int GetRabbitAttempt(IDictionary<string, object?>? headers) => throw new NotImplementedException();
}
