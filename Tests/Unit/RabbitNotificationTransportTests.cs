using System.Text;
using BLL.Configuration;
using BLL.Services.Notifications;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace Tests.Unit;

public sealed class RabbitNotificationTransportTests
{
    [Fact]
    public async Task DeclaresCompatibleMainAndReliableBoundedRetryTopology()
    {
        var queues = new Dictionary<string, IDictionary<string, object?>?>();
        var channel = new Mock<IChannel>();
        channel.Setup(c => c.QueueDeclareAsync(It.IsAny<string>(), true, false, false,
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string queue, bool _, bool _, bool _, IDictionary<string, object?>? arguments, bool _, bool _, CancellationToken _) =>
            { queues[queue] = arguments; return new QueueDeclareOk(queue, 0, 0); });
        await RabbitNotificationTopology.DeclareAsync(channel.Object, "TaskReminders", new RabbitMqOptions { RetryDelayMilliseconds = 1000 });
        Assert.Null(queues["TaskReminders"]);
        var retry = queues["TaskReminders.retry"]!;
        Assert.Equal("quorum", retry["x-queue-type"]);
        Assert.Equal("at-least-once", retry["x-dead-letter-strategy"]);
        Assert.Equal("reject-publish", retry["x-overflow"]);
        Assert.Equal(1000, retry["x-message-ttl"]);
        Assert.Equal("", retry["x-dead-letter-exchange"]);
        Assert.Equal("TaskReminders", retry["x-dead-letter-routing-key"]);
        Assert.InRange(Convert.ToInt64(retry["x-max-length-bytes"]), 65536, 64 * 1024 * 1024);
        Assert.NotNull(queues["TaskReminders.dead"]);
        Assert.Contains(channel.Invocations, invocation => invocation.Method.Name == "ExchangeDeclareAsync"
            && Equals(invocation.Arguments[0], RabbitNotificationTopology.DeadLetterExchange));
        Assert.Contains(channel.Invocations, invocation => invocation.Method.Name == "QueueBindAsync"
            && Equals(invocation.Arguments[0], "TaskReminders.dead")
            && Equals(invocation.Arguments[1], RabbitNotificationTopology.DeadLetterExchange));
    }

    [Fact]
    public async Task RetryCopiesOriginalBodyAndIdentityAndStripsUntrustedHeadersBeforeAcknowledgement()
    {
        using var h = new Harness();
        await h.Settlement.RetryAsync(2, CancellationToken.None);
        var publication = Assert.Single(h.Published);
        Assert.Equal("", publication.Exchange);
        Assert.Equal("TaskReminders.retry", publication.Queue);
        Assert.True(publication.Mandatory);
        Assert.Equal(h.Delivery.BasicProperties.MessageId, publication.Properties.MessageId);
        Assert.Equal(DeliveryModes.Persistent, publication.Properties.DeliveryMode);
        Assert.Equal(h.Delivery.Body.ToArray(), publication.Body);
        Assert.Equal(1, publication.Properties.Headers![NotificationDeliveryMetadata.RetryCountHeader]);
        Assert.False(publication.Properties.Headers.ContainsKey("Authorization"));
        Assert.False(publication.Properties.Headers.ContainsKey("x-death"));
        Assert.Equal(["confirmed", "ack"], h.Events);
        h.Consumer.Verify(c => c.BasicAckAsync(17, false, It.IsAny<CancellationToken>()), Times.Once);
        h.Consumer.Verify(c => c.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OriginalDeliveryCannotBeAcknowledgedWhileRetryConfirmationIsPending()
    {
        using var h = new Harness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.BeforeConfirm = async () => { entered.SetResult(); await confirmed.Task; };
        var retry = h.Settlement.RetryAsync(3, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            h.Consumer.Verify(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { confirmed.TrySetResult(); }
        await retry;
        Assert.Equal(["confirmed", "ack"], h.Events);
    }

    [Fact]
    public async Task RetryConfirmationFailureLeavesOriginalUnacknowledged()
    {
        using var h = new Harness();
        h.BeforeConfirm = () => throw new IOException("synthetic broker confirm failure");
        await Assert.ThrowsAsync<IOException>(() => h.Settlement.RetryAsync(2, CancellationToken.None));
        h.Consumer.Verify(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(h.Events);
    }

    [Fact]
    public async Task DeadLetterIsPersistentConfirmedAndRetainsIdentity()
    {
        using var h = new Harness();
        await h.Settlement.DeadLetterAsync("InvalidNotification", CancellationToken.None);
        var publication = Assert.Single(h.Published);
        Assert.Equal(RabbitNotificationTopology.DeadLetterExchange, publication.Exchange);
        Assert.Equal("TaskReminders.dead", publication.Queue);
        Assert.True(publication.Mandatory);
        Assert.Equal(h.Delivery.BasicProperties.MessageId, publication.Properties.MessageId);
        Assert.Equal(DeliveryModes.Persistent, publication.Properties.DeliveryMode);
        Assert.Equal(["confirmed", "ack"], h.Events);
    }

    [Fact]
    public async Task DeadLetterConfirmationFailureLeavesOriginalUnacknowledged()
    {
        using var h = new Harness();
        h.BeforeConfirm = () => throw new IOException("synthetic dead queue unavailable");
        await Assert.ThrowsAsync<IOException>(() => h.Settlement.DeadLetterAsync("AttemptsExhausted", CancellationToken.None));
        h.Consumer.Verify(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuccessfulCompletionUsesOnlySingleDeliveryAcknowledgement()
    {
        using var h = new Harness();
        await h.Settlement.CompleteAsync(CancellationToken.None);
        h.Consumer.Verify(c => c.BasicAckAsync(17, false, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(h.Published);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task RetryCannotResetOrExceedThreeBusinessAttempts(int attempt)
    {
        using var h = new Harness();
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Settlement.RetryAsync(attempt, CancellationToken.None));
        Assert.Empty(h.Published);
        Assert.Empty(h.Events);
    }

    private sealed record Publication(string Exchange, string Queue, bool Mandatory, BasicProperties Properties, byte[] Body);
    private sealed class Harness : IDisposable
    {
        private readonly SemaphoreSlim _publishLock = new(1, 1);
        public Mock<IChannel> Consumer { get; } = new();
        public Mock<IChannel> Publisher { get; } = new();
        public List<Publication> Published { get; } = [];
        public List<string> Events { get; } = [];
        public Func<Task>? BeforeConfirm { get; set; }
        public BasicDeliverEventArgs Delivery { get; }
        public RabbitNotificationSettlement Settlement { get; }
        public Harness()
        {
            var id = Guid.NewGuid().ToString("N");
            Delivery = new("test", 17, false, "", "TaskReminders", new BasicProperties
            {
                MessageId = id, CorrelationId = id,
                Headers = new Dictionary<string, object?>
                { [NotificationDeliveryMetadata.RetryCountHeader] = 0, ["Authorization"] = "synthetic secret", ["x-death"] = "untrusted" }
            }, Encoding.UTF8.GetBytes($"{{\"CorrelationId\":\"{id}\",\"TaskName\":\"Перевірити\"}}"), CancellationToken.None);
            Publisher.Setup(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), true,
                    It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((string exchange, string queue, bool mandatory, BasicProperties properties, ReadOnlyMemory<byte> body, CancellationToken _) =>
                    new ValueTask(PublishAsync(exchange, queue, mandatory, properties, body)));
            Consumer.Setup(c => c.BasicAckAsync(17, false, It.IsAny<CancellationToken>()))
                .Returns(() => { Events.Add("ack"); return ValueTask.CompletedTask; });
            Settlement = new(Consumer.Object, Publisher.Object, _publishLock, "TaskReminders", Delivery);
        }
        private async Task PublishAsync(string exchange, string queue, bool mandatory, BasicProperties properties, ReadOnlyMemory<byte> body)
        {
            Published.Add(new(exchange, queue, mandatory, properties, body.ToArray()));
            if (BeforeConfirm is not null) await BeforeConfirm();
            Events.Add("confirmed");
        }
        public void Dispose() => _publishLock.Dispose();
    }
}
