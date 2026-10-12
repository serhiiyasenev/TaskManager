#nullable enable
using Azure.Messaging.ServiceBus;
using BLL.Configuration;
using BLL.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Azure.Identity;
using BLL.Services.Notifications;

namespace BLL.Services;

public sealed class AzureServiceBusQueueService(
    IOptions<ServiceBusOptions> options, ServiceBusClient client,
    ILogger<AzureServiceBusQueueService> logger) : IQueueService, IAsyncDisposable
{
    private readonly ServiceBusOptions _options = options.Value;
    private readonly Dictionary<string, ServiceBusSender> _senders = new(StringComparer.Ordinal);
    private readonly object _senderLock = new();
    private int _disposed;

    public async Task<bool> PostValue(string message, string? queueName = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            _options.Validate();
            var target = string.IsNullOrWhiteSpace(queueName) ? _options.QueueName : queueName;
            if (target != _options.QueueName && target != _options.ReminderQueueName) return false;
            var encoded = NotificationPayloadCodec.Encode(message);
            ServiceBusSender sender;
            lock (_senderLock)
            {
                if (_disposed != 0) return false;
                if (!_senders.TryGetValue(target, out sender!))
                    _senders[target] = sender = client.CreateSender(target);
            }
            var outgoing = new ServiceBusMessage(new BinaryData(encoded.Body))
            {
                MessageId = encoded.MessageId.ToString("N"), CorrelationId = encoded.MessageId.ToString("N"),
                ContentType = "application/json"
            };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            await sender.SendMessageAsync(outgoing, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("Service Bus notification publish did not receive confirmation.");
            return false;
        }
    }
    public async ValueTask DisposeAsync()
    {
        ServiceBusSender[] senders;
        lock (_senderLock)
        {
            if (_disposed != 0) return;
            _disposed = 1; senders = _senders.Values.ToArray(); _senders.Clear();
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var sender in senders)
        {
            try { await sender.CloseAsync(timeout.Token); }
            catch (Exception) { logger.LogWarning("Service Bus sender close did not complete during shutdown."); }
        }
        // Shared client disposal belongs to the host's DI lifetime.
    }
}

public static class ServiceBusClientFactory
{
    public static ServiceBusClient Create(ServiceBusOptions options)
    {
        options.Validate();
        var identity = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId);
        var credentialOptions = new ManagedIdentityCredentialOptions(identity);
        credentialOptions.Diagnostics.IsLoggingEnabled = false;
        credentialOptions.Diagnostics.IsLoggingContentEnabled = false;
        credentialOptions.Diagnostics.IsAccountIdentifierLoggingEnabled = false;
        return new ServiceBusClient(options.FullyQualifiedNamespace, new ManagedIdentityCredential(credentialOptions), new ServiceBusClientOptions
        {
            Identifier = "TaskManager.Notifications",
            RetryOptions = new ServiceBusRetryOptions
            {
                MaxRetries = 2, Delay = TimeSpan.FromMilliseconds(250), MaxDelay = TimeSpan.FromSeconds(1),
                TryTimeout = TimeSpan.FromSeconds(options.TryTimeoutSeconds)
            }
        });
    }
}
