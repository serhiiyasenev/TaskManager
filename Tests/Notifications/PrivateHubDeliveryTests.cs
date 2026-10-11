extern alias NotifierLib;
using System.Security.Claims;
using BLL.Models.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NotifierLib::Notifier;
using NotifierLib::Notifier.Notifications;
using Xunit;

namespace Tests.Notifications;

public sealed class PrivateHubDeliveryTests
{
    [Fact]
    public async Task ConnectionJoinsOnlyCurrentServerComputedPrivateGroup()
    {
        var recipient = new Mock<INotificationRecipientScope>();
        recipient.Setup(value => value.GetGroupAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync("current-scope");
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
        recipient.Setup(value => value.GetGroupAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync("scope-after-revocation");
        using var provider = new ServiceCollection().AddScoped(_ => recipient.Object).BuildServiceProvider();
        var messages = new List<(string Method, object?[] Arguments)>();
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(value => value.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, arguments, _) => messages.Add((method, arguments)))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(value => value.Group("scope-after-revocation")).Returns(proxy.Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.SetupGet(value => value.Clients).Returns(clients.Object);
        var id = Guid.NewGuid();
        await new InboxRealtimePublisher(provider.GetRequiredService<IServiceScopeFactory>(), hub.Object)
            .PublishAsync(id, 7);
        var hint = Assert.Single(messages.Where(value => value.Method == "InboxChanged"));
        Assert.Equal(id, Assert.IsType<NotificationHintDto>(Assert.Single(hint.Arguments)).NotificationId);
        var legacy = Assert.Single(messages.Where(value => value.Method == "ReceiveMessage"));
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

    private static HubCallerContext Caller(int id)
    {
        var caller = new Mock<HubCallerContext>();
        caller.SetupGet(value => value.ConnectionId).Returns("connection");
        caller.SetupGet(value => value.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Synthetic")));
        return caller.Object;
    }
}
