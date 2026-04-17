extern alias NotifierLib;
using System.Text.Json;
using BLL.Models.Messaging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client.Events;
using Xunit;
using NotifierLib::Notifier;

namespace Tests.Integration;

public class NotifierListenerIntegrationTests
{
    private static (RabbitMqListenerService listener, Mock<IHubContext<ChatHub>> hubContextMock, Mock<ILogger<RabbitMqListenerService>> loggerMock, Mock<IClientProxy> clientProxyMock) CreateListener()
    {
        var clientProxy = new Mock<IClientProxy>();
        clientProxy.Setup(p => p.SendCoreAsync("ReceiveMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<ChatHub>>();
        hubContext.Setup(c => c.Clients).Returns(clients.Object);

        var logger = new Mock<ILogger<RabbitMqListenerService>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:QueueName"] = "TestQueue",
                ["RabbitMQ:ReminderQueueName"] = "TaskReminders"
            })
            .Build();

        var listener = new RabbitMqListenerService(hubContext.Object, logger.Object, config);
        return (listener, hubContext, logger, clientProxy);
    }

    [Fact]
    public async Task HandleMessageAsync_PublishesReminderToHub()
    {
        var sentMessages = new List<(string user, string message)>();

        var (listener, hubContextMock, _, clientProxy) = CreateListener();
        clientProxy.Setup(p => p.SendCoreAsync("ReceiveMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) => sentMessages.Add(((string)args[0]!, (string)args[1]!)))
            .Returns(Task.CompletedTask);

        var envelope = new TaskNotificationEnvelope
        {
            Type = NotificationEventType.TaskReminder,
            Reminder = new TaskReminderNotification(42, "Demo", 7, 1, "Proj", DateTime.UtcNow.AddMinutes(5), 10, 30)
        };

        var message = JsonSerializer.Serialize(envelope);
        await listener.HandleMessageAsyncForTests("TaskReminders", message, CancellationToken.None);

        Assert.Single(sentMessages);
        var (user, text) = sentMessages.Single();
        Assert.Equal("Reminder", user);
        Assert.Contains("Upcoming task", text);
        Assert.Contains("Demo", text);
    }

    [Fact]
    public async Task HandleMessageAsync_LogsEmailForOverdueAndBroadcasts()
    {
        var sentMessages = new List<(string user, string message)>();
        var (listener, hubContextMock, loggerMock, clientProxy) = CreateListener();
        clientProxy.Setup(p => p.SendCoreAsync("ReceiveMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) => sentMessages.Add(((string)args[0]!, (string)args[1]!)))
            .Returns(Task.CompletedTask);

        var envelope = new TaskNotificationEnvelope
        {
            Type = NotificationEventType.TaskOverdueEscalation,
            Reminder = new TaskReminderNotification(9, "Overdue", 5, 2, "Proj", DateTime.UtcNow.AddMinutes(-30), 15, 20)
        };

        await listener.HandleMessageAsyncForTests("TaskReminders", JsonSerializer.Serialize(envelope), CancellationToken.None);

        Assert.Single(sentMessages);
        var (user, message) = sentMessages.Single();
        Assert.Equal("Overdue", user);
        Assert.Contains("OVERDUE", message);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Simulated email notification")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_PublishesDefaultMessage_WhenPlainString()
    {
        var sentMessages = new List<(string user, string message)>();
        var (listener, hubContextMock, _, clientProxy) = CreateListener();
        clientProxy.Setup(p => p.SendCoreAsync("ReceiveMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) => sentMessages.Add(((string)args[0]!, (string)args[1]!)))
            .Returns(Task.CompletedTask);

        await listener.HandleMessageAsyncForTests("TestQueue", "Task 123 done", CancellationToken.None);

        Assert.Single(sentMessages);
        var (user, message) = sentMessages.Single();
        Assert.Equal("RabbitMQ Default User", user);
        Assert.Contains("Task \"Task 123 done\" was marked as Done", message);
    }
}
