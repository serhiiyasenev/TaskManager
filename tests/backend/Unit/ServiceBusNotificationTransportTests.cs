extern alias NotifierLib;
using System.Text;
using Azure.Messaging.ServiceBus;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Services;
using BLL.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NotifierLib::Notifier;
using Xunit;

namespace Tests.Unit;

public sealed class ServiceBusNotificationTransportTests
{
    private static readonly Guid Id = Guid.Parse("7e135e80-b0ee-458a-90a6-430561228dbc");
    private static string Body => $"{{\"CorrelationId\":\"{Id:N}\",\"TaskName\":\"Перевірити 🚀\"}}";
    private static ServiceBusOptions Configuration() => new()
    { FullyQualifiedNamespace = "synthetic.servicebus.windows.net", QueueName = "TaskNotifications", ReminderQueueName = "TaskReminders" };

    [Fact]
    public async Task SenderReportsSuccessOnlyAfterActualSdkSendCompletesWithStableMessageId()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirm = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ServiceBusMessage? sent = null;
        var sender = new Mock<ServiceBusSender>();
        sender.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Returns(async (ServiceBusMessage message, CancellationToken ct) => { sent = message; entered.SetResult(); await confirm.Task.WaitAsync(ct); });
        var client = new Mock<ServiceBusClient>();
        client.Setup(c => c.CreateSender("TaskNotifications")).Returns(sender.Object);
        await using var publisher = new AzureServiceBusQueueService(Options.Create(Configuration()), client.Object, NullLogger<AzureServiceBusQueueService>.Instance);
        var work = publisher.PostValue(Body);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(work.IsCompleted);
        }
        finally { confirm.TrySetResult(); }
        Assert.True(await work);
        Assert.NotNull(sent);
        Assert.Equal(Id.ToString("N"), sent.MessageId);
        Assert.Equal(Body, sent.Body.ToString());
        Assert.Equal("application/json", sent.ContentType);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"CorrelationId\":\"not-a-guid\"}")]
    [InlineData("plain legacy text")]
    public async Task SenderRejectsMalformedNotificationIdentityInsteadOfGeneratingFallback(string body)
    {
        var client = new Mock<ServiceBusClient>();
        await using var publisher = new AzureServiceBusQueueService(Options.Create(Configuration()), client.Object, NullLogger<AzureServiceBusQueueService>.Instance);
        Assert.False(await publisher.PostValue(body));
        client.Verify(c => c.CreateSender(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SenderRejectsOversizeAndUnconfiguredQueueBeforeSdkSend()
    {
        var client = new Mock<ServiceBusClient>();
        await using var publisher = new AzureServiceBusQueueService(Options.Create(Configuration()), client.Object, NullLogger<AzureServiceBusQueueService>.Instance);
        Assert.False(await publisher.PostValue(new string('x', 65537)));
        Assert.False(await publisher.PostValue(Body, "other-product-queue"));
        client.Verify(c => c.CreateSender(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SenderBrokerFailureReturnsFalseAndCallerCancellationPropagates()
    {
        var sender = new Mock<ServiceBusSender>();
        sender.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("synthetic broker unavailable", ServiceBusFailureReason.ServiceCommunicationProblem));
        var client = new Mock<ServiceBusClient>(); client.Setup(c => c.CreateSender("TaskNotifications")).Returns(sender.Object);
        await using var publisher = new AzureServiceBusQueueService(Options.Create(Configuration()), client.Object, NullLogger<AzureServiceBusQueueService>.Instance);
        Assert.False(await publisher.PostValue(Body));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PostValue(Body, ct: cancellation.Token));
        sender.Verify(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessorUsesPeekLockManualCompletionAndBoundedConcurrency()
    {
        using var h = new ReceiveHarness();
        var optionsSeen = new TaskCompletionSource<ServiceBusProcessorOptions>(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new Mock<ServiceBusProcessor>();
        processor.Setup(p => p.StartProcessingAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        processor.Setup(p => p.StopProcessingAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        processor.Setup(p => p.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        h.Client.Setup(c => c.CreateProcessor(It.IsAny<string>(), It.IsAny<ServiceBusProcessorOptions>()))
            .Returns((string _, ServiceBusProcessorOptions options) => { optionsSeen.TrySetResult(options); return processor.Object; });
        using var listener = h.Listener;
        await listener.StartAsync(CancellationToken.None);
        var seen = await optionsSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(ServiceBusReceiveMode.PeekLock, seen.ReceiveMode);
        Assert.False(seen.AutoCompleteMessages);
        Assert.Equal(0, seen.PrefetchCount);
        Assert.InRange(seen.MaxConcurrentCalls, 1, 8);
        Assert.InRange(seen.MaxAutoLockRenewalDuration.TotalSeconds, 60, 300);
        await listener.StopAsync(CancellationToken.None);
        processor.Verify(p => p.StartProcessingAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        processor.Verify(p => p.StopProcessingAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessorCompletesActualSdkMessageOnlyAfterCommittedInboxResult()
    {
        using var h = new ReceiveHarness();
        h.Processor.Setup(p => p.ProcessAsync(Id, Body, It.IsAny<CancellationToken>()))
            .Returns(() => { h.Events.Add("SQL committed"); return Task.FromResult(new NotificationProcessingResult(Id, 7, true, false)); });
        await h.Listener.HandleMessageAsyncForTests(h.Args(Body, 1));
        Assert.Equal("SQL committed", h.Events[0]);
        Assert.Contains("complete", h.Events);
        h.Receiver.Verify(r => r.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        h.Receiver.Verify(r => r.AbandonMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public async Task ServiceBusDeliveryCountBoundsSqlFailureRetries(int deliveryCount, bool deadLetter)
    {
        using var h = new ReceiveHarness();
        h.Processor.Setup(p => p.ProcessAsync(Id, Body, It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("synthetic SQL unavailable"));
        await h.Listener.HandleMessageAsyncForTests(h.Args(Body, deliveryCount));
        if (deadLetter)
            h.Receiver.Verify(r => r.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), "AttemptsExhausted", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        else
            h.Receiver.Verify(r => r.AbandonMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
        h.Receiver.Verify(r => r.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidUtf8MessageGoesToSafeDlqWithoutSqlOrCompletion()
    {
        using var h = new ReceiveHarness();
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(body: new BinaryData(new byte[] { 0xC3, 0x28 }),
            messageId: Id.ToString("N"), deliveryCount: 1, lockedUntil: DateTimeOffset.UtcNow.AddMinutes(1));
        await h.Listener.HandleMessageAsyncForTests(new ProcessMessageEventArgs(received, h.Receiver.Object, "synthetic", CancellationToken.None));
        h.Receiver.Verify(r => r.DeadLetterMessageAsync(received, "InvalidNotification", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        h.Processor.Verify(p => p.ProcessAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("https://synthetic.servicebus.windows.net")]
    [InlineData("synthetic.servicebus.windows.net/path")]
    [InlineData("user:password@synthetic.servicebus.windows.net")]
    [InlineData("localhost")]
    public void UnsafeNamespaceIsRejectedBeforeCreatingSdkClient(string namespaceValue) => Assert.Throws<InvalidOperationException>(() =>
        ServiceBusClientFactory.Create(new ServiceBusOptions { FullyQualifiedNamespace = namespaceValue }));

    private sealed class ReceiveHarness : IDisposable
    {
        private readonly ServiceProvider _provider;
        public Mock<ServiceBusClient> Client { get; } = new();
        public Mock<ServiceBusReceiver> Receiver { get; } = new();
        public Mock<INotificationMessageProcessor> Processor { get; } = new();
        public List<string> Events { get; } = [];
        public ServiceBusListenerService Listener { get; }
        public ReceiveHarness()
        {
            Processor.Setup(p => p.ProcessAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NotificationProcessingResult(Id, 7, false, false));
            _provider = new ServiceCollection().AddScoped(_ => Processor.Object).BuildServiceProvider();
            var hints = new Mock<IInboxRealtimePublisher>(); hints.Setup(p => p.PublishAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var coordinator = new NotificationDeliveryCoordinator(_provider.GetRequiredService<IServiceScopeFactory>(), hints.Object, NullLogger<NotificationDeliveryCoordinator>.Instance);
            Listener = new(Options.Create(Configuration()), Client.Object, coordinator, NullLogger<ServiceBusListenerService>.Instance);
            Receiver.Setup(r => r.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()))
                .Returns(() => { Events.Add("complete"); return Task.CompletedTask; });
            Receiver.Setup(r => r.AbandonMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Receiver.Setup(r => r.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }
        public ProcessMessageEventArgs Args(string body, int count) => new(ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes(body)), messageId: Id.ToString("N"), deliveryCount: count,
            lockedUntil: DateTimeOffset.UtcNow.AddMinutes(1)), Receiver.Object, "synthetic", CancellationToken.None);
        public void Dispose() { Listener.Dispose(); _provider.Dispose(); }
    }
}
