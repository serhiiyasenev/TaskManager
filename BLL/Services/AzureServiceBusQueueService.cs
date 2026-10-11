#nullable enable
using Azure.Messaging.ServiceBus;
using BLL.Configuration;
using BLL.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BLL.Services;

public sealed class AzureServiceBusQueueService(
    IOptions<ServiceBusOptions> options, ServiceBusClient client,
    ILogger<AzureServiceBusQueueService> logger) : IQueueService, IAsyncDisposable
{
    public Task<bool> PostValue(string message, string? queueName = null, CancellationToken ct = default) => throw new NotImplementedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public static class ServiceBusClientFactory
{
    public static ServiceBusClient Create(ServiceBusOptions options) => throw new NotImplementedException();
}
