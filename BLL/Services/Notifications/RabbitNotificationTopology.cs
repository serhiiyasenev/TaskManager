#nullable enable
using BLL.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Security.Cryptography;
using System.Text.Json;

namespace BLL.Services.Notifications;

public static class RabbitNotificationTopology
{
    public const string DeadLetterExchange = "TaskManager.Notifications.Dead";
    public const int MaxQueueBytes = 16 * 1024 * 1024;
    public static async Task DeclareAsync(IChannel channel, string mainQueue, RabbitMqOptions options,
        CancellationToken ct = default)
    {
        if (!options.Durable || options.RetryAttempts != 3 || options.RetryDelayMilliseconds is < 100 or > 60000
            || string.IsNullOrWhiteSpace(mainQueue) || mainQueue.Length > 249 || mainQueue.StartsWith("amq.", StringComparison.Ordinal)
            || mainQueue.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_' or ':')))
            throw new InvalidOperationException("RabbitMQ notification topology configuration is invalid.");
        // Existing durable classic queues keep precisely their original arguments.
        await channel.QueueDeclareAsync(mainQueue, true, false, false, arguments: null, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, true, false, cancellationToken: ct);
        await channel.QueueDeclareAsync(mainQueue + ".dead", true, false, false,
            new Dictionary<string, object?> { ["x-queue-type"] = "quorum", ["x-overflow"] = "reject-publish", ["x-max-length-bytes"] = MaxQueueBytes },
            cancellationToken: ct);
        await channel.QueueBindAsync(mainQueue + ".dead", DeadLetterExchange, mainQueue + ".dead", cancellationToken: ct);
        await channel.QueueDeclareAsync(mainQueue + ".retry", true, false, false,
            new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum", ["x-message-ttl"] = options.RetryDelayMilliseconds,
                ["x-dead-letter-exchange"] = "", ["x-dead-letter-routing-key"] = mainQueue,
                ["x-dead-letter-strategy"] = "at-least-once", ["x-overflow"] = "reject-publish",
                ["x-max-length-bytes"] = MaxQueueBytes
            }, cancellationToken: ct);
    }
}

public sealed class RabbitNotificationSettlement(
    IChannel consumerChannel, IChannel publisherChannel, SemaphoreSlim publisherLock,
    string queueName, BasicDeliverEventArgs delivery) : INotificationDeliverySettlement
{
    private readonly byte[] _body = delivery.Body.ToArray();
    public async Task CompleteAsync(CancellationToken ct) => await consumerChannel.BasicAckAsync(delivery.DeliveryTag, false, ct);
    public Task RetryAsync(int nextAttempt, CancellationToken ct)
    {
        if (nextAttempt is < 2 or > 3) throw new InvalidDataException("Notification retry attempt is invalid.");
        return PublishThenCompleteAsync("", queueName + ".retry", new Dictionary<string, object?>
        { [NotificationDeliveryMetadata.RetryCountHeader] = nextAttempt - 1 }, _body, ct);
    }
    public Task DeadLetterAsync(string safeReason, CancellationToken ct)
    {
        if (safeReason is not ("InvalidNotification" or "AttemptsExhausted"))
            throw new InvalidDataException("Notification dead-letter reason is invalid.");
        // An oversized poison message must not saturate the bounded dead queue.
        // Its quarantine record retains a digest/size, never a partial private body.
        var body = _body.Length <= NotificationPayloadCodec.MaxUtf8Bytes ? _body : JsonSerializer.SerializeToUtf8Bytes(new
        { reason = safeReason, originalBytes = _body.Length, sha256 = Convert.ToHexString(SHA256.HashData(_body)) });
        return PublishThenCompleteAsync(RabbitNotificationTopology.DeadLetterExchange, queueName + ".dead",
            new Dictionary<string, object?> { ["x-taskmanager-failure"] = safeReason }, body, ct);
    }

    private async Task PublishThenCompleteAsync(string exchange, string target, IDictionary<string, object?> headers,
        byte[] body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await publisherLock.WaitAsync(timeout.Token);
        try
        {
            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent, MessageId = delivery.BasicProperties.MessageId,
                CorrelationId = delivery.BasicProperties.CorrelationId, ContentType = "application/json", Headers = headers
            };
            await publisherChannel.BasicPublishAsync(exchange, target, true, properties, body, timeout.Token);
        }
        finally { publisherLock.Release(); }
        await CompleteAsync(ct);
    }
}
