using System.Text.Json;
using BLL.Models.Messaging;
using Microsoft.EntityFrameworkCore;
using Moq;
using RabbitMQ.Client;
using Tests.Notifications;
using Xunit;

namespace Tests.Unit;

public sealed class NotifierPrivateRoutingTests
{
    [Fact]
    public async Task ExecutedTaskCommitsInboxBeforeRoutingOnlyToCurrentPersistedRecipient()
    {
        await using var h = await DurableListenerHarness.CreateAsync();
        var envelope = await h.IntentAsync(NotificationEventType.ExecutedTaskCompleted);
        await h.DeliverAsync(envelope);
        Assert.Equal(7, Assert.Single(h.Hints.Delivered).Recipient);
        await using var db = h.Context();
        Assert.Equal(1, await db.UserNotifications.CountAsync());
        Assert.Equal(1, await db.NotificationProcessingMarkers.CountAsync());
        h.Consumer.Verify(channel => channel.BasicAckAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReminderRoutesOnlyValidatedRecipientAfterDurableCommit()
    {
        await using var h = await DurableListenerHarness.CreateAsync();
        var envelope = await h.IntentAsync();
        await h.DeliverAsync(envelope);
        Assert.Equal(7, Assert.Single(h.Hints.Delivered).Recipient);
        Assert.Empty(h.PublishedQueues);
    }

    [Fact]
    public async Task LegacyPayloadWithoutIdentityIsQuarantinedWithoutNotification()
    {
        await using var h = await DurableListenerHarness.CreateAsync();
        await h.DeliverAsync("Task 123 done", null);
        Assert.Empty(h.Hints.Delivered);
        Assert.Equal(["TestQueue.dead"], h.PublishedQueues);
        await using var db = h.Context();
        Assert.Empty(await db.UserNotifications.ToListAsync());
    }

    [Fact]
    public async Task SpoofedOrNonPositiveRecipientCannotBecomeInboxOrRealtimeDelivery()
    {
        await using var h = await DurableListenerHarness.CreateAsync();
        var envelope = await h.IntentAsync();
        envelope.Reminder = envelope.Reminder! with { PerformerId = 0 };
        await h.DeliverAsync(JsonSerializer.Serialize(envelope), envelope.CorrelationId);
        Assert.Empty(h.Hints.Delivered);
        Assert.Equal(["TestQueue.dead"], h.PublishedQueues);
        await using var db = h.Context();
        Assert.Empty(await db.UserNotifications.ToListAsync());
    }
}
