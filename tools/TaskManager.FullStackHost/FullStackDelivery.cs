using System.Collections.Concurrent;
using BLL.Interfaces;
using BLL.Services.Notifications;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace TaskManager.FullStackHost;

internal sealed class SettlementEvidence(FullStackDatabase database)
{
    private readonly ConcurrentDictionary<Guid, byte> _acknowledged = new();
    public long Count => _acknowledged.Count;
    public async Task RecordAsync(Guid id, CancellationToken ct)
    {
        await database.AssertCommittedMarkerAsync(id, ct);
        _acknowledged.TryAdd(id, 0);
    }
}

internal sealed class ControlledQueue(FullStackSettings settings, SettlementEvidence evidence) : IQueueService
{
    private NotificationDeliveryCoordinator? _coordinator;
    public bool Ready => Volatile.Read(ref _coordinator) is not null;
    public void Bind(NotificationDeliveryCoordinator coordinator) => Volatile.Write(ref _coordinator, coordinator);

    public async Task<bool> PostValue(string message, string? queueName = null, CancellationToken ct = default)
    {
        if (queueName != settings.TaskQueue && queueName != settings.ReminderQueue) return false;
        var coordinator = Volatile.Read(ref _coordinator) ?? throw new InvalidOperationException("Controlled delivery is not ready.");
        var encoded = NotificationPayloadCodec.Encode(message);
        var settlement = new ControlledSettlement(encoded.MessageId, evidence);
        await coordinator.ProcessAsync(new(encoded.MessageId.ToString("N"), encoded.Body), settlement, ct);
        return settlement.Completed;
    }

    private sealed class ControlledSettlement(Guid messageId, SettlementEvidence evidence) : INotificationDeliverySettlement
    {
        public bool Completed { get; private set; }
        public async Task CompleteAsync(CancellationToken ct)
        {
            // This query is a separate native SQL context: no uncommitted marker
            // can masquerade as durable settlement evidence.
            await evidence.RecordAsync(messageId, ct);
            Completed = true;
        }
        public Task RetryAsync(int nextAttempt, CancellationToken ct) => Task.CompletedTask;
        public Task DeadLetterAsync(string safeReason, CancellationToken ct) => Task.CompletedTask;
    }
}

internal sealed class ControlledBrokerHealth(ControlledQueue queue) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(queue.Ready ? HealthCheckResult.Healthy("Explicit controlled test transport ready.")
            : HealthCheckResult.Unhealthy("Explicit controlled test transport unavailable."));
}

// Coordinator calls realtime only after native settlement has completed.
// RabbitMQ evidence therefore counts unique created/acknowledged messages,
// without replacing the actual producer, listener or ACK implementation.
internal sealed class ObservedRealtimePublisher(IInboxRealtimePublisher actual, SettlementEvidence evidence) : IInboxRealtimePublisher
{
    public async Task PublishAsync(Guid notificationId, int recipientId, CancellationToken ct = default)
    {
        await evidence.RecordAsync(notificationId, ct);
        await actual.PublishAsync(notificationId, recipientId, ct);
    }
}

internal static class OwnedRabbitQueues
{
    public static async Task DeleteAsync(FullStackSettings settings, CancellationToken ct)
    {
        if (settings.Transport != "RabbitMQ") return;
        if (!settings.TaskQueue.StartsWith(settings.DatabaseName + ".", StringComparison.Ordinal)
            || !settings.ReminderQueue.StartsWith(settings.DatabaseName + ".", StringComparison.Ordinal))
            throw new InvalidOperationException("Broker cleanup requires owned queues.");
        var factory = new ConnectionFactory { HostName = settings.RabbitHost, Port = settings.RabbitPort,
            UserName = settings.RabbitUser, Password = settings.RabbitPassword, AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5) };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        foreach (var queue in new[] { settings.TaskQueue, settings.ReminderQueue })
            foreach (var suffix in new[] { ".retry", ".dead", "" })
                await channel.QueueDeleteAsync(queue + suffix, false, false, cancellationToken: ct);
    }
}
