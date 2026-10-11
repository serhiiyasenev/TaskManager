extern alias NotifierLib;
using System.Security.Claims;
using BLL.Models.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NotifierLib::Notifier;
using NotifierLib::Notifier.Notifications;
using Xunit;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Tests.Integration;
using Task = System.Threading.Tasks.Task;

namespace Tests.Notifications;

public sealed class PrivateHubDeliveryTests
{
    [Fact]
    public async Task ConnectionJoinsOnlyCurrentServerComputedPrivateGroup()
    {
        var recipient = new Mock<INotificationRecipientScope>();
        recipient.Setup(value => value.GetGroupAsync(7, It.IsAny<CancellationToken>(), null)).ReturnsAsync("current-scope");
        var groups = new Mock<IGroupManager>();
        groups.Setup(value => value.AddToGroupAsync("connection", "current-scope", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var hub = new ChatHub(recipient.Object) { Context = Caller(7), Groups = groups.Object };
        await hub.OnConnectedAsync();
        groups.Verify(value => value.AddToGroupAsync("connection", "current-scope", It.IsAny<CancellationToken>()), Times.Once);
        groups.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task MissingOrRevokedUserCannotJoinAnyPrivateGroup(int id)
    {
        var hub = new ChatHub(Mock.Of<INotificationRecipientScope>())
            { Context = Caller(id), Groups = Mock.Of<IGroupManager>() };
        await Assert.ThrowsAsync<HubException>(() => hub.OnConnectedAsync());
        Mock.Get(hub.Groups).VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublisherResolvesFreshScopeAndOnlySendsMinimalHintAndGenericLegacyText()
    {
        var recipient = new Mock<INotificationRecipientScope>();
        recipient.Setup(value => value.GetGroupAsync(101, It.IsAny<CancellationToken>(), null)).ReturnsAsync("scope-after-revocation");
        var id = Guid.NewGuid();
        using var factory = await InboxFactoryAsync(id, recipient);
        var messages = new List<(string Method, object?[] Arguments)>();
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(value => value.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, arguments, _) => messages.Add((method, arguments)))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(value => value.Group("scope-after-revocation")).Returns(proxy.Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.SetupGet(value => value.Clients).Returns(clients.Object);
        await new InboxRealtimePublisher(factory.Services.GetRequiredService<IServiceScopeFactory>(), hub.Object)
            .PublishAsync(id, 101);
        var hint = Assert.Single(messages, value => value.Method == "InboxChanged");
        Assert.Equal(id, Assert.IsType<NotificationHintDto>(Assert.Single(hint.Arguments)).NotificationId);
        var legacy = Assert.Single(messages, value => value.Method == "ReceiveMessage");
        Assert.Equal(new object?[] { "TaskManager", "Your notification inbox changed." }, legacy.Arguments);
        clients.Verify(value => value.User(It.IsAny<string>()), Times.Never);
        clients.VerifyGet(value => value.All, Times.Never);
    }

    [Fact]
    public async Task RevokedPublisherScopeSuppressesAllDelivery()
    {
        using var provider = new ServiceCollection().AddScoped(_ => Mock.Of<INotificationRecipientScope>()).BuildServiceProvider();
        var hub = new Mock<IHubContext<ChatHub>>();
        await new InboxRealtimePublisher(provider.GetRequiredService<IServiceScopeFactory>(), hub.Object)
            .PublishAsync(Guid.NewGuid(), 7);
        hub.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("reassigned")]
    [InlineData("task-deleted")]
    [InlineData("project-moved")]
    public async Task CommittedNotificationLosesVisibilityBeforeHint_NoPrivateDelivery(string mutation)
    {
        var recipient = new Mock<INotificationRecipientScope>();
        recipient.Setup(value => value.GetGroupAsync(101, It.IsAny<CancellationToken>(), null)).ReturnsAsync("current-scope");
        var id = Guid.NewGuid();
        using var factory = await InboxFactoryAsync(id, recipient);
        await factory.WithDatabaseAsync(async db =>
        {
            if (mutation == "reassigned") (await db.Tasks.FindAsync(101))!.PerformerId = 102;
            if (mutation == "task-deleted") db.Tasks.Remove((await db.Tasks.FindAsync(101))!);
            if (mutation == "project-moved") (await db.Projects.FindAsync(101))!.TeamId = 102;
            await db.SaveChangesAsync();
        });
        var hub = new Mock<IHubContext<ChatHub>>();
        var clients = new Mock<IHubClients>();
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(value => value.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        clients.Setup(value => value.Group(It.IsAny<string>())).Returns(proxy.Object);
        hub.SetupGet(value => value.Clients).Returns(clients.Object);
        await new InboxRealtimePublisher(factory.Services.GetRequiredService<IServiceScopeFactory>(), hub.Object).PublishAsync(id, 101);
        proxy.VerifyNoOtherCalls();
        clients.Verify(value => value.Group(It.IsAny<string>()), Times.Never);
    }

    private static async Task<DataIsolationFactory> InboxFactoryAsync(Guid id, Mock<INotificationRecipientScope> recipient)
    {
        var factory = await DataIsolationFactory.CreateAsync(configureServices: services => services.AddScoped(_ => recipient.Object));
        await factory.WithDatabaseAsync(async db =>
        {
            db.NotificationProcessingMarkers.Add(new NotificationProcessingMarker { MessageId = id,
                CanonicalPayloadHash = new string('A', 64), ProcessedAtUtc = DateTime.UtcNow });
            db.UserNotifications.Add(new UserNotification { Id = id, OwnerId = 101, TaskId = 101, ProjectId = 101,
                Kind = 3, TaskName = "Team A task", ProjectName = "Team A project", OccurredAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        return factory;
    }

    private static HubCallerContext Caller(int id)
    {
        var caller = new Mock<HubCallerContext>();
        caller.SetupGet(value => value.ConnectionId).Returns("connection");
        caller.SetupGet(value => value.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Synthetic")));
        return caller.Object;
    }
}
