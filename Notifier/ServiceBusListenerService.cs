using Azure.Messaging.ServiceBus;
using BLL.Configuration;
using BLL.Services.Notifications;
using Microsoft.Extensions.Options;

namespace Notifier;

public class ServiceBusListenerService(
    IOptions<ServiceBusOptions> options, ServiceBusClient client,
    NotificationDeliveryCoordinator coordinator, ILogger<ServiceBusListenerService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => throw new NotImplementedException();
    internal Task HandleMessageAsyncForTests(ProcessMessageEventArgs message) => throw new NotImplementedException();
}
