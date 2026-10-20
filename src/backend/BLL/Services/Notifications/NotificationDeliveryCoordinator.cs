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
        CancellationToken ct = default) => ProcessCoreAsync(delivery, settlement, ct);

    private async Task ProcessCoreAsync(NotificationDelivery delivery, INotificationDeliverySettlement settlement, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (delivery.Attempt > 3)
        {
            await settlement.DeadLetterAsync("AttemptsExhausted", ct);
            return;
        }
        NotificationProcessingResult result;
        try
        {
            var decoded = NotificationPayloadCodec.Decode(delivery);
            if (delivery.Attempt < 1) throw new InvalidDataException("Notification delivery attempt is invalid.");
            await using var scope = scopeFactory.CreateAsyncScope();
            result = await scope.ServiceProvider.GetRequiredService<INotificationMessageProcessor>()
                .ProcessAsync(decoded.MessageId, decoded.Payload, ct);
            if (result.MessageId != decoded.MessageId
                || (result.Created && !result.Suppressed && result.RecipientId is not > 0))
                throw new InvalidDataException("Notification processing outcome is invalid.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (InvalidDataException)
        {
            await settlement.DeadLetterAsync("InvalidNotification", ct);
            return;
        }
        catch (Exception)
        {
            // Exceptions can contain connection strings or payload excerpts.
            logger.LogWarning("Notification processing failed at business attempt {Attempt}.", delivery.Attempt);
            if (delivery.Attempt >= 3) await settlement.DeadLetterAsync("AttemptsExhausted", ct);
            else await settlement.RetryAsync(delivery.Attempt + 1, ct);
            return;
        }

        // Settlement failure is ambiguous after SQL commit. It must propagate,
        // rather than publishing a fresh retry lineage or changing the ID.
        await settlement.CompleteAsync(ct);
        if (!result.Created || result.Suppressed || result.RecipientId is not > 0 || ct.IsCancellationRequested) return;
        using var hintTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        hintTimeout.CancelAfter(TimeSpan.FromSeconds(2));
        try { await realtime.PublishAsync(result.MessageId, result.RecipientId.Value, hintTimeout.Token).WaitAsync(hintTimeout.Token); }
        catch (Exception)
        {
            // SQL/reconnect is the delivery guarantee; realtime is an optional hint.
            logger.LogWarning("Realtime hint was unavailable for notification {NotificationId}.", result.MessageId);
        }
    }
}

public static class NotificationDeliveryMetadata
{
    public const string RetryCountHeader = "x-taskmanager-retry-count";
    public static int GetRabbitAttempt(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(RetryCountHeader, out var value)) return 1;
        var count = value switch
        {
            byte number => (long)number, sbyte number => number,
            short number => number, ushort number => number,
            int number => number, uint number => number, long number => number,
            _ => -1
        };
        if (count is < 0 or > 2) throw new InvalidDataException("Notification retry header is invalid.");
        return (int)count + 1;
    }
}
