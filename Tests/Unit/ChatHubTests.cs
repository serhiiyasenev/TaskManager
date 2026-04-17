extern alias NotifierLib;
using Microsoft.AspNetCore.SignalR;
using Moq;
using NotifierLib::Notifier;
using Xunit;

namespace Tests.Unit;

public class ChatHubTests
{
    [Fact]
    public async Task SendMessage_BroadcastsToAllClients()
    {
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(
                "ReceiveMessage",
                It.Is<object?[]>(args => (string)args[0]! == "user" && (string)args[1]! == "hello"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>();
        clients.Setup(c => c.All).Returns(proxy.Object);

        var hub = new ChatHub();
        typeof(Hub).GetProperty("Clients")!.SetValue(hub, clients.Object);

        await hub.SendMessage("user", "hello");

        proxy.Verify(p => p.SendCoreAsync(
                "ReceiveMessage",
                It.Is<object?[]>(args => (string)args[0]! == "user" && (string)args[1]! == "hello"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
