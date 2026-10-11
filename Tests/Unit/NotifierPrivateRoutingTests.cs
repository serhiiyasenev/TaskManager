extern alias NotifierLib;
using System.Text.Json;
using BLL.Models.Messaging;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task ExecutedTask_ResolvesRecipientFromPersistedTask()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<TaskContext>(options => options.UseInMemoryDatabase(databaseName));
        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TaskContext>();
            database.Teams.Add(new DAL.Entities.Team { Id = 1, Name = "Synthetic team", CreatedAt = DateTime.UtcNow });
            database.Users.Add(new DAL.Entities.User("recipient", "Synthetic", "Member", "recipient@example.test") { Id = 6, TeamId = 1 });
            database.Projects.Add(new DAL.Entities.Project { Id = 1, AuthorId = 6, TeamId = 1, Name = "Synthetic project", Description = "Synthetic test", CreatedAt = DateTime.UtcNow, Deadline = DateTime.UtcNow.AddDays(1) });
            database.Tasks.Add(new DAL.Entities.Task { Id = 99, ProjectId = 1, PerformerId = 6, Name = "Private task", Description = "Synthetic test", CreatedAt = DateTime.UtcNow });
            await database.SaveChangesAsync();
        }
        var (listener, clients) = CreateListener(provider.GetRequiredService<IServiceScopeFactory>());
        var envelope = new TaskNotificationEnvelope
        {
            Type = NotificationEventType.ExecutedTaskCompleted,
            ExecutedTask = new ExecutedTaskNotification(99, "Private task", DateTime.UtcNow)
        };
        await listener.HandleMessageAsyncForTests("TestQueue", JsonSerializer.Serialize(envelope), CancellationToken.None);
        clients.Verify(c => c.User("6"), Times.Once);
        clients.VerifyGet(c => c.All, Times.Never);
    }

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

    private static (RabbitMqListenerService, Mock<IHubClients>) CreateListener(IServiceScopeFactory? scopeFactory = null)
    {
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.SetupGet(c => c.All).Returns(proxy.Object);
        clients.Setup(c => c.User(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        return (new RabbitMqListenerService(hub.Object, NullLogger<RabbitMqListenerService>.Instance,
            new ConfigurationBuilder().Build(), scopeFactory: scopeFactory), clients);
    }
}
