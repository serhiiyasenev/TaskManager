extern alias NotifierLib;
using System.Text.Json;
using BLL.Models.Messaging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RabbitMQ.Client.Events;
using Xunit;
using NotifierLib::Notifier;

namespace Tests.Integration;

public class NotifierListenerIntegrationTests
{
    [Fact]
    public async Task HandleMessageAsync_PublishesReminderToHub()
    {
        var sentMessages = new List<(string user, string message)>();

        var clientProxy = new Mock<IClientProxy>();
        clientProxy.Setup(p => p.SendCoreAsync("ReceiveMessage", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) =>
            {
                sentMessages.Add(((string)args[0]!, (string)args[1]!));
            })
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<ChatHub>>();
        hubContext.Setup(c => c.Clients).Returns(clients.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:QueueName"] = "TestQueue",
                ["RabbitMQ:ReminderQueueName"] = "TaskReminders"
            })
            .Build();

        var listener = new RabbitMqListenerService(hubContext.Object, NullLogger<RabbitMqListenerService>.Instance, config);

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
}
