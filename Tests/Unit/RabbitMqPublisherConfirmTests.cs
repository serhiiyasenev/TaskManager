using System.Text;
using BLL.Configuration;
using BLL.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Polly;
using RabbitMQ.Client;
using Xunit;

namespace Tests.Unit;

public class RabbitMqPublisherConfirmTests
{
    [Fact]
    public async Task Retry_UsesTrackedConfirmsMandatoryRoutingAndSameMessageId()
    {
        var ids = new List<string>();
        var channel = new Mock<IChannel>();
        channel.SetupGet(c => c.IsOpen).Returns(true);
        channel.Setup(c => c.BasicPublishAsync(
                "", "TaskReminders", true, It.IsAny<BasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, bool _, BasicProperties properties, ReadOnlyMemory<byte> _, CancellationToken _) =>
            {
                ids.Add(properties.MessageId!);
                return ids.Count == 1 ? ValueTask.FromException(new IOException("Transient broker failure")) : ValueTask.CompletedTask;
            });
        var connection = new Mock<IConnection>();
        connection.SetupGet(c => c.IsOpen).Returns(true);
        connection.Setup(c => c.CreateChannelAsync(
                It.Is<CreateChannelOptions>(o => o.PublisherConfirmationsEnabled && o.PublisherConfirmationTrackingEnabled),
                It.IsAny<CancellationToken>())).ReturnsAsync(channel.Object);
        var factory = new Mock<IConnectionFactory>();
        factory.Setup(f => f.CreateConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        await using var publisher = new RabbitMqService(
            Options.Create(new RabbitMqOptions()), NullLogger<RabbitMqService>.Instance,
            Policy.Handle<IOException>().RetryAsync(1), factory.Object);

        Assert.True(await publisher.PostValue("{\"CorrelationId\":\"stable-occurrence\"}", "TaskReminders"));
        Assert.Equal(new[] { "stable-occurrence", "stable-occurrence" }, ids);
        connection.Verify(c => c.CreateChannelAsync(
            It.Is<CreateChannelOptions>(o => o.PublisherConfirmationsEnabled && o.PublisherConfirmationTrackingEnabled),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CanceledPublish_IsNotReportedAsSuccess()
    {
        await using var publisher = new RabbitMqService(Options.Create(new RabbitMqOptions()),
            NullLogger<RabbitMqService>.Instance, Policy.NoOpAsync());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PostValue("message", ct: cts.Token));
    }
}
