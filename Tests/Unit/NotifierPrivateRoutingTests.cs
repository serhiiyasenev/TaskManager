extern alias NotifierLib;
using System.Text.Json;
using BLL.Models.Messaging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NotifierLib::Notifier;
using Xunit;

namespace Tests.Unit;

public class NotifierPrivateRoutingTests
{
    [Fact]
    public async Task Reminder_RoutesOnlyToPerformer_NoGlobalOrOtherUserDelivery()
    {
        var (listener, clients) = CreateListener();
        var envelope = Reminder(7);
        await listener.HandleMessageAsyncForTests("TaskReminders", JsonSerializer.Serialize(envelope), CancellationToken.None);
        clients.Verify(c => c.User("7"), Times.Once);
        clients.Verify(c => c.User(It.Is<string>(id => id != "7")), Times.Never);
        clients.VerifyGet(c => c.All, Times.Never);
    }

    [Fact]
    public async Task LegacyPayloadWithoutRecipient_IsRejectedWithoutBroadcast()
    {
        var (listener, clients) = CreateListener();
        await Assert.ThrowsAsync<InvalidDataException>(() => listener.HandleMessageAsyncForTests("TestQueue", "Task 123 done", CancellationToken.None));
        clients.VerifyGet(c => c.All, Times.Never);
        clients.Verify(c => c.User(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task NonPositiveRecipient_IsRejectedWithoutDelivery()
    {
        var (listener, clients) = CreateListener();
        await Assert.ThrowsAsync<InvalidDataException>(() => listener.HandleMessageAsyncForTests("TaskReminders", JsonSerializer.Serialize(Reminder(0)), CancellationToken.None));
        clients.VerifyGet(c => c.All, Times.Never);
        clients.Verify(c => c.User(It.IsAny<string>()), Times.Never);
    }

    private static TaskNotificationEnvelope Reminder(int userId) => new()
    {
        Type = NotificationEventType.TaskReminder,
        Reminder = new TaskReminderNotification(42, "Private task", userId, 1, "Private project", DateTime.UtcNow, 10, 30)
    };

    private static (RabbitMqListenerService, Mock<IHubClients>) CreateListener()
    {
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.SetupGet(c => c.All).Returns(proxy.Object);
        clients.Setup(c => c.User(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        return (new RabbitMqListenerService(hub.Object, NullLogger<RabbitMqListenerService>.Instance,
            new ConfigurationBuilder().Build()), clients);
    }
}
