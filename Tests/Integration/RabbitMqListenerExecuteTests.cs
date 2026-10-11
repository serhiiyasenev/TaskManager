extern alias NotifierLib;
using System.Text;
using System.Text.Json;
using BLL.Models.Messaging;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NotifierLib::Notifier;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace Tests.Integration;

public class RabbitMqListenerExecuteTests
{
    [Fact]
    public async Task ExecuteAsync_ConsumesMessagesAndRoutesNotificationsToTaskPerformer()
    {
        var sentMessages = new List<(string user, string message)>();
        var consumerReady = new TaskCompletionSource<AsyncEventingBasicConsumer>(TaskCreationOptions.RunContinuationsAsynchronously);

        var (listener, clientProxy, loggerMock, connectionFactoryMock, channelMock) = CreateListener(sentMessages, consumerReady);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var runTask = listener.RunExecuteAsync(cts.Token);

        var consumer = await consumerReady.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var envelope = new TaskNotificationEnvelope
        {
            Type = NotificationEventType.ExecutedTaskCompleted,
            ExecutedTask = new ExecutedTaskNotification(99, "Integration Task", DateTime.UtcNow)
        };

        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        await consumer.HandleBasicDeliverAsync(
            consumerTag: "tag",
            deliveryTag: 1,
            redelivered: false,
            exchange: string.Empty,
            routingKey: "TestQueue",
            properties: new BasicProperties(),
            body: new ReadOnlyMemory<byte>(payload),
            cancellationToken: CancellationToken.None);

        await Task.Delay(50, CancellationToken.None);
        cts.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(1));

        channelMock.Verify(c => c.BasicAckAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
        channelMock.Verify(c => c.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(sentMessages, m => m.user == "Notifier" && m.message.Contains("was marked as Done"));
        channelMock.Verify(c => c.QueueDeclareAsync(
            "TestQueue",
            true,
            false,
            false,
            null,
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
        connectionFactoryMock.Verify(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Listening on queue")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_RequeuesOnSignalRFailure_WithoutAcknowledging()
    {
        var ready = new TaskCompletionSource<AsyncEventingBasicConsumer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (listener, client, _, _, channel) = CreateListener(new List<(string, string)>(), ready);
        client.Setup(p => p.SendCoreAsync("ReceiveMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SignalR unavailable"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = listener.RunExecuteAsync(cts.Token);
        var consumer = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var envelope = new TaskNotificationEnvelope
        {
            Type = NotificationEventType.TaskReminder,
            Reminder = new TaskReminderNotification(99, "Private reminder", 7, 1, "Project", DateTime.UtcNow, 10, 30)
        };
        await consumer.HandleBasicDeliverAsync("tag", 7, false, "", "TestQueue", new BasicProperties(),
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope)), CancellationToken.None);
        channel.Verify(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        channel.Verify(c => c.BasicNackAsync(7, false, true, It.IsAny<CancellationToken>()), Times.Once);
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsUnrouteableLegacyDeliveryWithoutRetryOrAck()
    {
        var ready = new TaskCompletionSource<AsyncEventingBasicConsumer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (listener, _, _, _, channel) = CreateListener(new List<(string, string)>(), ready);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = listener.RunExecuteAsync(cts.Token);
        var consumer = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.HandleBasicDeliverAsync("tag", 8, false, "", "TestQueue", new BasicProperties(),
            Encoding.UTF8.GetBytes("Legacy message without recipient"), CancellationToken.None);
        channel.Verify(c => c.BasicNackAsync(8, false, false, It.IsAny<CancellationToken>()), Times.Once);
        channel.Verify(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        channel.Verify(c => c.BasicNackAsync(8, false, true, It.IsAny<CancellationToken>()), Times.Never);
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static (TestableRabbitMqListenerService listener, Mock<IClientProxy> clientProxy, Mock<ILogger<RabbitMqListenerService>> loggerMock, Mock<IConnectionFactory> factoryMock, Mock<IChannel> channelMock) CreateListener(
        List<(string user, string message)> messages,
        TaskCompletionSource<AsyncEventingBasicConsumer> consumerReady)
    {
        var clientProxy = new Mock<IClientProxy>();
        clientProxy.Setup(p => p.SendCoreAsync("ReceiveMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) => messages.Add(((string)args[0]!, (string)args[1]!)))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.User(It.IsAny<string>())).Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<ChatHub>>();
        hubContext.Setup(c => c.Clients).Returns(clients.Object);

        var logger = new Mock<ILogger<RabbitMqListenerService>>();

        var channel = new Mock<IChannel>();
        channel.Setup(c => c.QueueDeclareAsync(
                It.IsAny<string>(),
                true,
                false,
                false,
                null,
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string queue, bool _, bool _, bool _, IDictionary<string, object>? _, bool _, bool _, CancellationToken _) =>
                new QueueDeclareOk(queue, 0, 0));

        channel.Setup(c => c.BasicConsumeAsync(
                It.IsAny<string>(),
                false,
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<IAsyncBasicConsumer>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object?>?, IAsyncBasicConsumer, CancellationToken>(
                (_, _, _, _, _, _, consumer, _) => consumerReady.TrySetResult((AsyncEventingBasicConsumer)consumer))
            .ReturnsAsync("consumer-tag");

        channel.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var connection = new Mock<IConnection>();
        connection.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel.Object);
        connection.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var factory = new Mock<IConnectionFactory>();
        factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:QueueName"] = "TestQueue",
                ["RabbitMQ:ReminderQueueName"] = "ReminderQueue",
                ["RabbitMQ:UserName"] = "guest",
                ["RabbitMQ:Password"] = "guest",
                ["RabbitMQ:HostName"] = "localhost",
                ["RabbitMQ:Port"] = "5672"
            })
            .Build();

        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<TaskContext>(options => options.UseInMemoryDatabase(databaseName));
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TaskContext>();
            database.Teams.Add(new DAL.Entities.Team { Id = 1, Name = "Synthetic team", CreatedAt = DateTime.UtcNow });
            database.Users.Add(new DAL.Entities.User("recipient", "Synthetic", "Member", "recipient@example.test") { Id = 7, TeamId = 1 });
            database.Projects.Add(new DAL.Entities.Project { Id = 1, AuthorId = 7, TeamId = 1, Name = "Synthetic project", Description = "Synthetic test", CreatedAt = DateTime.UtcNow, Deadline = DateTime.UtcNow.AddDays(1) });
            database.Tasks.Add(new DAL.Entities.Task { Id = 99, ProjectId = 1, PerformerId = 7, Name = "Integration Task", Description = "Synthetic test", CreatedAt = DateTime.UtcNow });
            database.SaveChanges();
        }
        var listener = new TestableRabbitMqListenerService(hubContext.Object, logger.Object, config, factory.Object,
            provider.GetRequiredService<IServiceScopeFactory>());
        return (listener, clientProxy, logger, factory, channel);
    }

    private sealed class TestableRabbitMqListenerService : RabbitMqListenerService
    {
        public TestableRabbitMqListenerService(
            IHubContext<ChatHub> hubContext,
            ILogger<RabbitMqListenerService> logger,
            IConfiguration configuration,
            IConnectionFactory connectionFactory,
            IServiceScopeFactory scopeFactory)
            : base(hubContext, logger, configuration, connectionFactory, scopeFactory)
        {
        }

        public Task RunExecuteAsync(CancellationToken token) => ExecuteAsync(token);
    }
}
