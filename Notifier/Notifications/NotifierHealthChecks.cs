using Azure.Messaging.ServiceBus;
using BLL.Configuration;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Notifier.Notifications;

public sealed class NotifierSqlHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TaskContext>();
            return await database.Database.CanConnectAsync(timeout.Token) ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Notification storage unavailable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return HealthCheckResult.Unhealthy("Notification storage unavailable."); }
    }
}

public sealed class NotificationBrokerHealthCheck(IServiceProvider services,
    IOptions<NotificationTransportOptions> transport, IOptions<RabbitMqOptions> rabbit,
    IConnectionFactory? connectionFactory = null) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            if (transport.Value.Provider == "AzureServiceBus")
            {
                var options = services.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
                var client = services.GetRequiredService<ServiceBusClient>();
                // Peek tests receiver permission and network without consuming
                // a business message or requiring management-plane privileges.
                var queues = new HashSet<string>(StringComparer.Ordinal) { options.QueueName };
                if (!string.IsNullOrWhiteSpace(options.ReminderQueueName)) queues.Add(options.ReminderQueueName);
                foreach (var queue in queues)
                {
                    await using var receiver = client.CreateReceiver(queue, new ServiceBusReceiverOptions
                        { ReceiveMode = ServiceBusReceiveMode.PeekLock, PrefetchCount = 0 });
                    await receiver.PeekMessageAsync(cancellationToken: timeout.Token);
                }
            }
            else
            {
                var settings = rabbit.Value;
                var factory = connectionFactory ?? new ConnectionFactory
                {
                    HostName = settings.HostName, Port = settings.Port, UserName = settings.UserName, Password = settings.Password,
                    AutomaticRecoveryEnabled = false, RequestedConnectionTimeout = TimeSpan.FromSeconds(2)
                };
                await using var connection = await factory.CreateConnectionAsync(timeout.Token);
                if (!connection.IsOpen) return HealthCheckResult.Unhealthy("Notification broker unavailable.");
            }
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return HealthCheckResult.Unhealthy("Notification broker unavailable."); }
    }
}
