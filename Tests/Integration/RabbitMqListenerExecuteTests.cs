using System.Text;
using System.Text.Json;
using BLL.Models.Messaging;
using Microsoft.EntityFrameworkCore;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Tests.Notifications;
using Xunit;

namespace Tests.Integration;

public sealed class RabbitMqListenerExecuteTests
{
    [Fact]
    public async Task ExecuteAsyncUsesCompatibleTopologyTrackedPublisherAndSqlBeforeAck()
    {
        var ready = new TaskCompletionSource<AsyncEventingBasicConsumer>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var h = await DurableListenerHarness.CreateAsync(ready);
        var envelope = await h.IntentAsync(NotificationEventType.ExecutedTaskCompleted);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await h.Listener.StartAsync(cancellation.Token);
        try
        {
            var consumer = await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await DeliverAsync(consumer, envelope, 1);
            h.Consumer.Verify(channel => channel.BasicAckAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
            h.Consumer.Verify(channel => channel.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Single(h.Hints.Delivered);
            await using var db = h.Context();
            Assert.Equal(1, await db.UserNotifications.CountAsync());
            h.Consumer.Verify(channel => channel.QueueDeclareAsync("TestQueue", true, false, false, null,
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { cancellation.Cancel(); await h.Listener.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task ExecuteAsyncHintFailureKeepsSqlCommitAndAcknowledgesWithoutRetry()
    {
        var ready = new TaskCompletionSource<AsyncEventingBasicConsumer>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var h = await DurableListenerHarness.CreateAsync(ready);
        var envelope = await h.IntentAsync();
        h.Hints.Failure = new IOException("synthetic SignalR unavailable");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await h.Listener.StartAsync(cancellation.Token);
        try
        {
            var consumer = await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await DeliverAsync(consumer, envelope, 7);
            h.Consumer.Verify(channel => channel.BasicAckAsync(7, false, It.IsAny<CancellationToken>()), Times.Once);
            Assert.Empty(h.PublishedQueues);
            await using var db = h.Context();
            Assert.Equal(1, await db.UserNotifications.CountAsync());
        }
        finally { cancellation.Cancel(); await h.Listener.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task ExecuteAsyncInvalidLegacyPayloadUsesConfirmedDeadQueueThenAck()
    {
        var ready = new TaskCompletionSource<AsyncEventingBasicConsumer>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var h = await DurableListenerHarness.CreateAsync(ready);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await h.Listener.StartAsync(cancellation.Token);
        try
        {
            var consumer = await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await consumer.HandleBasicDeliverAsync("test", 8, false, "", "TestQueue", new BasicProperties(),
                Encoding.UTF8.GetBytes("Legacy message without identity"), CancellationToken.None);
            Assert.Equal(["TestQueue.dead"], h.PublishedQueues);
            h.Consumer.Verify(channel => channel.BasicAckAsync(8, false, It.IsAny<CancellationToken>()), Times.Once);
            h.Consumer.Verify(channel => channel.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Empty(h.Hints.Delivered);
        }
        finally { cancellation.Cancel(); await h.Listener.StopAsync(CancellationToken.None); }
    }

    private static Task DeliverAsync(AsyncEventingBasicConsumer consumer, TaskNotificationEnvelope envelope, ulong tag) =>
        consumer.HandleBasicDeliverAsync("test", tag, false, "", "TestQueue", new BasicProperties
        { MessageId = envelope.CorrelationId, CorrelationId = envelope.CorrelationId },
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope)), CancellationToken.None);
}
