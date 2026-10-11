using Azure.Messaging.ServiceBus;
using BLL.Configuration;
using BLL.Services.Notifications;
using Microsoft.Extensions.Options;

namespace Notifier;

public class ServiceBusListenerService(
    IOptions<ServiceBusOptions> options, ServiceBusClient client,
    NotificationDeliveryCoordinator coordinator, ILogger<ServiceBusListenerService> logger) : BackgroundService
{
    private int _connected;
    public bool IsConnected => Volatile.Read(ref _connected) == 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        settings.Validate();
        var queues = new HashSet<string>(StringComparer.Ordinal) { settings.QueueName };
        if (!string.IsNullOrWhiteSpace(settings.ReminderQueueName)) queues.Add(settings.ReminderQueueName);
        var processors = new List<ServiceBusProcessor>();
        try
        {
            foreach (var queue in queues)
            {
                var processor = client.CreateProcessor(queue, new ServiceBusProcessorOptions
                {
                    ReceiveMode = ServiceBusReceiveMode.PeekLock, AutoCompleteMessages = false,
                    PrefetchCount = 0, MaxConcurrentCalls = settings.MaxConcurrentCalls,
                    MaxAutoLockRenewalDuration = TimeSpan.FromSeconds(settings.MaxAutoLockRenewalSeconds),
                    Identifier = "TaskManager.Notifications"
                });
                processors.Add(processor);
                processor.ProcessMessageAsync += HandleMessageAsyncForTests;
                processor.ProcessErrorAsync += error =>
                {
                    Volatile.Write(ref _connected, 0);
                    logger.LogWarning("Service Bus processor reported an error at {ErrorSource}.", error.ErrorSource);
                    return Task.CompletedTask;
                };
                await processor.StartProcessingAsync(stoppingToken);
            }
            Volatile.Write(ref _connected, 1);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            Volatile.Write(ref _connected, 0);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            foreach (var processor in processors)
            {
                try
                {
                    await processor.StopProcessingAsync(timeout.Token);
                    await processor.DisposeAsync().AsTask().WaitAsync(timeout.Token);
                }
                catch (Exception) { logger.LogWarning("Service Bus processor close did not complete during shutdown."); }
            }
        }
    }

    internal Task HandleMessageAsyncForTests(ProcessMessageEventArgs message)
    {
        Volatile.Write(ref _connected, 1);
        return coordinator.ProcessAsync(new NotificationDelivery(message.Message.MessageId,
            message.Message.Body.ToMemory(), message.Message.DeliveryCount), new Settlement(message), message.CancellationToken);
    }

    private sealed class Settlement(ProcessMessageEventArgs message) : INotificationDeliverySettlement
    {
        public Task CompleteAsync(CancellationToken ct) => message.CompleteMessageAsync(message.Message, ct);
        public async Task RetryAsync(int nextAttempt, CancellationToken ct)
        {
            if (nextAttempt is < 2 or > 3) throw new InvalidDataException("Notification retry attempt is invalid.");
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            // Broker DeliveryCount is authoritative; no republish/reset of count.
            await message.AbandonMessageAsync(message.Message, cancellationToken: ct);
        }
        public Task DeadLetterAsync(string safeReason, CancellationToken ct) =>
            message.DeadLetterMessageAsync(message.Message, safeReason, "Notification delivery could not be processed.", ct);
    }
}
