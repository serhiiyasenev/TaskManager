#nullable enable
using BLL.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BLL.Services.Notifications;

public static class RabbitNotificationTopology
{
    public const string DeadLetterExchange = "TaskManager.Notifications.Dead";
    public static Task DeclareAsync(IChannel channel, string mainQueue, RabbitMqOptions options,
        CancellationToken ct = default) => throw new NotImplementedException();
}

public sealed class RabbitNotificationSettlement(
    IChannel consumerChannel, IChannel publisherChannel, SemaphoreSlim publisherLock,
    string queueName, BasicDeliverEventArgs delivery) : INotificationDeliverySettlement
{
    public Task CompleteAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task RetryAsync(int nextAttempt, CancellationToken ct) => throw new NotImplementedException();
    public Task DeadLetterAsync(string safeReason, CancellationToken ct) => throw new NotImplementedException();
}
