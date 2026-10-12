extern alias NotifierLib;
using Azure.Messaging.ServiceBus;
using BLL.Configuration;
using BLL.Services.Notifications;
using Microsoft.Azure.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq;
using NotifierLib::Notifier.Notifications;
using RabbitMQ.Client;
using Xunit;

namespace Tests.Notifications;

public sealed class NotificationHostTests
{
    [Fact]
    public async Task RabbitProbeIsBoundedAndDoesNotExposePrivateConnectionFailures()
    {
        var connection = new Mock<IConnectionFactory>();
        connection.Setup(value => value.CreateConnectionAsync(It.Is<CancellationToken>(token => token.CanBeCanceled)))
            .ThrowsAsync(new IOException("amqp://private-user:synthetic-password@private-server"));
        using var services = new ServiceCollection().BuildServiceProvider();
        var probe = new NotificationBrokerHealthCheck(services, Options.Create(new NotificationTransportOptions()),
            Options.Create(new RabbitMqOptions()), connection.Object);
        var result = await probe.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Notification broker unavailable.", result.Description);
        Assert.Null(result.Exception);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task ServiceBusProbeUsesPeekWithoutConsumingOrRequiringAdministration()
    {
        var receiver = new Mock<ServiceBusReceiver>();
        receiver.Setup(value => value.PeekMessageAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceBusReceivedMessage)null!);
        var client = new Mock<ServiceBusClient>();
        client.Setup(value => value.CreateReceiver("private-notifications", It.Is<ServiceBusReceiverOptions>(options =>
            options.ReceiveMode == ServiceBusReceiveMode.PeekLock && options.PrefetchCount == 0))).Returns(receiver.Object);
        using var services = new ServiceCollection().AddSingleton(client.Object)
            .AddSingleton<IOptions<ServiceBusOptions>>(Options.Create(new ServiceBusOptions { QueueName = "private-notifications" }))
            .BuildServiceProvider();
        var probe = new NotificationBrokerHealthCheck(services, Options.Create(new NotificationTransportOptions { Provider = "AzureServiceBus" }),
            Options.Create(new RabbitMqOptions()));
        Assert.Equal(HealthStatus.Healthy, (await probe.CheckHealthAsync(new HealthCheckContext())).Status);
        receiver.Verify(value => value.PeekMessageAsync(null, It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
        receiver.Verify(value => value.DisposeAsync(), Times.Once);
        receiver.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Unknown", null, null)]
    [InlineData("AzureSignalR", "http://private.service.signalr.net", null)]
    [InlineData("AzureSignalR", "https://private.service.signalr.net/path", null)]
    [InlineData("AzureSignalR", "https://arbitrary.example.test", null)]
    [InlineData("AzureSignalR", "https://private.service.signalr.net", "not-a-guid")]
    public void InvalidRealtimeConfigurationFailsClosed(string provider, string? endpoint, string? identity)
    {
        var configuration = Configuration(provider, endpoint, identity);
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddPrivateNotificationSignalR(configuration));
    }

    [Fact]
    public void AzureRealtimeUsesExplicitEndpointAndBoundedTokenWithoutConnectionString()
    {
        var configuration = Configuration("AzureSignalR", "https://private.service.signalr.net", null);
        var collection = new ServiceCollection().AddLogging().AddSingleton(configuration);
        collection.AddPrivateNotificationSignalR(configuration);
        using var services = collection.BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<ServiceOptions>>().Value;
        Assert.Null(options.ConnectionString);
        Assert.Single(options.Endpoints!);
        Assert.Equal("TaskManager", options.ApplicationName);
        Assert.Equal(TimeSpan.FromMinutes(5), options.AccessTokenLifetime);
    }

    [Fact]
    public void UnknownBrokerProviderCannotFallBackToRabbit()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Notifications:Provider"] = "misspelled-azure" }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddNotificationTransport(configuration));
    }

    private static IConfiguration Configuration(string provider, string? endpoint, string? identity) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Realtime:Provider"] = provider, ["Realtime:Endpoint"] = endpoint, ["Realtime:ManagedIdentityClientId"] = identity }).Build();
}
