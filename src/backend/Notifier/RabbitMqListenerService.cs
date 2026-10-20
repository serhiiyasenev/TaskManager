using BLL.Configuration;
using BLL.Services.Notifications;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Notifier;

public class RabbitMqListenerService(
    NotificationDeliveryCoordinator coordinator,
    ILogger<RabbitMqListenerService> logger,
    IOptions<RabbitMqOptions> options,
    IConnectionFactory? connectionFactory = null) : BackgroundService
{
    private int _connected;
    public bool IsConnected => Volatile.Read(ref _connected) == 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var factory = connectionFactory ?? new ConnectionFactory
        {
            HostName = settings.HostName, Port = settings.Port, UserName = settings.UserName, Password = settings.Password,
            ConsumerDispatchConcurrency = 1, AutomaticRecoveryEnabled = false
        };
        var queues = new HashSet<string>(StringComparer.Ordinal) { settings.QueueName };
        if (!string.IsNullOrWhiteSpace(settings.ReminderQueueName)) queues.Add(settings.ReminderQueueName);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var publisherLock = new SemaphoreSlim(1, 1);
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var consumerChannel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await using var publisherChannel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), stoppingToken);
                var reconnect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                foreach (var queue in queues)
                    await RabbitNotificationTopology.DeclareAsync(consumerChannel, queue, settings, stoppingToken);
                await consumerChannel.BasicQosAsync(0, 1, false, stoppingToken);
                foreach (var queue in queues)
                {
                    var consumer = new AsyncEventingBasicConsumer(consumerChannel);
                    consumer.ReceivedAsync += async (_, delivery) =>
                    {
                        try { await HandleDeliveryAsync(queue, delivery, consumerChannel, publisherChannel, publisherLock, stoppingToken); }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                        catch (Exception)
                        {
                            // Closing outside this callback requeues the unacknowledged
                            // delivery without deadlocking consumer dispatcher shutdown.
                            logger.LogWarning("Notification settlement unavailable; reconnecting the RabbitMQ consumer.");
                            reconnect.TrySetResult();
                        }
                    };
                    await consumerChannel.BasicConsumeAsync(queue, false, consumer, stoppingToken);
                    logger.LogInformation("Listening on queue {QueueName}.", queue);
                }
                Volatile.Write(ref _connected, 1);
                while (!stoppingToken.IsCancellationRequested && connection.IsOpen && consumerChannel.IsOpen && publisherChannel.IsOpen)
                {
                    await Task.WhenAny(reconnect.Task, Task.Delay(TimeSpan.FromSeconds(1), stoppingToken));
                    if (reconnect.Task.IsCompleted) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("RabbitMQ consumer unavailable; connection will be retried."); }
            finally { Volatile.Write(ref _connected, 0); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task HandleDeliveryAsync(string queueName, BasicDeliverEventArgs delivery,
        IChannel consumerChannel, IChannel publisherChannel, SemaphoreSlim publisherLock, CancellationToken ct)
    {
        var settlement = new RabbitNotificationSettlement(consumerChannel, publisherChannel, publisherLock, queueName, delivery);
        int attempt;
        try { attempt = NotificationDeliveryMetadata.GetRabbitAttempt(delivery.BasicProperties.Headers); }
        catch (InvalidDataException) { await settlement.DeadLetterAsync("InvalidNotification", ct); return; }
        // Delivery body is decoded within the awaited callback; it is never
        // retained after RabbitMQ releases its pooled delivery memory.
        await coordinator.ProcessAsync(new(delivery.BasicProperties.MessageId, delivery.Body, attempt), settlement, ct);
    }
}
