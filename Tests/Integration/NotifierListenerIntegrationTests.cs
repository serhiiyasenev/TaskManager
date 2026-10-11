using BLL.Models.Messaging;
using Microsoft.EntityFrameworkCore;
using Moq;
using RabbitMQ.Client;
using Tests.Notifications;
using Xunit;

namespace Tests.Integration;

public sealed class NotifierListenerIntegrationTests
{
    [Fact]
    public async Task PendingReminderIntentCanBeConsumedBeforePublisherRecordsItsConfirmation()
    {
        await using var h = await DurableListenerHarness.CreateAsync();
        var envelope = await h.IntentAsync();
        await using (var before = h.Context())
            Assert.Equal(DAL.Entities.NotificationOutboxStatus.Pending, (await before.NotificationOutbox.SingleAsync()).Status);
        await h.DeliverAsync(envelope);
        Assert.Equal(Guid.Parse(envelope.CorrelationId!), Assert.Single(h.Hints.Delivered).Id);
        await using var db = h.Context();
        Assert.Equal(1, await db.UserNotifications.CountAsync());
        h.Consumer.Verify(channel => channel.BasicAckAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OverdueIntentPersistsPrivateInboxAndSendsOnlyItsStableIdHint()
    {
        await using var h = await DurableListenerHarness.CreateAsync();
        var envelope = await h.IntentAsync(NotificationEventType.TaskOverdueEscalation);
        await h.DeliverAsync(envelope);
        var hint = Assert.Single(h.Hints.Delivered);
        Assert.Equal(Guid.Parse(envelope.CorrelationId!), hint.Id);
        Assert.Equal(7, hint.Recipient);
        await using var db = h.Context();
        Assert.Equal(1, await db.UserNotifications.CountAsync());
    }

    [Fact]
    public async Task RepeatedDeliveryKeepsOneInboxMarkerAndOneRealtimeHint()
    {
        await using var h = await DurableListenerHarness.CreateAsync();
        var envelope = await h.IntentAsync();
        await h.DeliverAsync(envelope, 1);
        await h.DeliverAsync(envelope, 2);
        Assert.Single(h.Hints.Delivered);
        await using var db = h.Context();
        Assert.Equal(1, await db.UserNotifications.CountAsync());
        Assert.Equal(1, await db.NotificationProcessingMarkers.CountAsync());
        h.Consumer.Verify(channel => channel.BasicAckAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
        h.Consumer.Verify(channel => channel.BasicAckAsync(2, false, It.IsAny<CancellationToken>()), Times.Once);
    }
}
