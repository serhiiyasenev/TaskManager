using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using BLL.Interfaces;
using BLL.Models.Messaging;
using BLL.Services.AI;
using BLL.Services.Notifications;
using DAL.Context;
using DAL.Entities;
using DAL.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.AI;
using Tests.Integration;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Notifications;

public sealed class NotificationInboxTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ValidEvent_CommitsOneMarkerAndNotificationBeforeProducerConfirmation(int kind)
    {
        using var factory = await CreateFactoryAsync();
        var id = await SeedEventAsync(factory, kind);
        var result = await ProcessAsync(factory, id);
        Assert.True(result.Created);
        Assert.False(result.Suppressed);
        Assert.Equal(101, result.RecipientId);
        await factory.WithDatabaseAsync(async db =>
        {
            var notification = Assert.Single(await db.Set<UserNotification>().ToListAsync());
            Assert.Equal(id, notification.Id);
            Assert.Equal(kind, notification.Kind);
            Assert.Equal(101, notification.OwnerId);
            Assert.Equal("Team A task", notification.TaskName);
            Assert.Equal("Team A project", notification.ProjectName);
            var marker = Assert.Single(await db.Set<NotificationProcessingMarker>().ToListAsync());
            Assert.Equal(id, marker.MessageId);
            Assert.Equal(NotificationProcessingDisposition.Delivered, marker.Disposition);
            Assert.Matches("^[A-Fa-f0-9]{64}$", marker.CanonicalPayloadHash);
            Assert.Equal(NotificationOutboxStatus.Pending, (await db.NotificationOutbox.SingleAsync()).Status);
        });
    }

    [Fact]
    public async Task AckLossDuplicate_WithPropertyReorderingAndRenamedTask_DoesNotCreateOrRepublish()
    {
        using var factory = await CreateFactoryAsync();
        var id = await SeedEventAsync(factory);
        Assert.True((await ProcessAsync(factory, id)).Created);
        await factory.WithDatabaseAsync(async db =>
        {
            (await db.Tasks.SingleAsync(value => value.Id == 101)).Name = "Current canonical task";
            (await db.Projects.SingleAsync(value => value.Id == 101)).Name = "Current canonical project";
            await db.SaveChangesAsync();
        });
        string payload = "";
        await factory.WithDatabaseAsync(async db => payload = (await db.NotificationOutbox.SingleAsync()).Payload);
        var parsed = JsonNode.Parse(payload)!.AsObject();
        var reordered = new JsonObject(parsed.Reverse().Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value?.DeepClone())));
        var result = await ProcessAsync(factory, id, reordered.ToJsonString());
        Assert.False(result.Created);
        Assert.False(result.Suppressed);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Single(await db.Set<UserNotification>().ToListAsync());
            Assert.Single(await db.Set<NotificationProcessingMarker>().ToListAsync());
        });
        using var client = factory.CreateUserClient(101);
        var item = (await ReadPageAsync(client)).GetProperty("items")[0];
        Assert.Equal("Current canonical task", item.GetProperty("taskName").GetString());
        Assert.Equal("Current canonical project", item.GetProperty("projectName").GetString());
    }

    [Fact]
    public async Task DuplicateIdWithChangedPayload_IsPermanentRejectionAndKeepsOriginalNotification()
    {
        using var factory = await CreateFactoryAsync();
        var id = await SeedEventAsync(factory);
        await ProcessAsync(factory, id);
        string payload = "";
        await factory.WithDatabaseAsync(async db => payload = (await db.NotificationOutbox.SingleAsync()).Payload);
        var changed = JsonNode.Parse(payload)!.AsObject();
        changed["TaskCreated"]!["TaskName"] = "Changed immutable payload";
        await Assert.ThrowsAsync<InvalidDataException>(() => ProcessAsync(factory, id, changed.ToJsonString()));
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Equal("Team A task", (await db.Set<UserNotification>().SingleAsync()).TaskName);
            Assert.Single(await db.Set<NotificationProcessingMarker>().ToListAsync());
        });
    }

    [Theory]
    [InlineData("json")]
    [InlineData("message-id")]
    [InlineData("unknown-kind")]
    [InlineData("multiple-bodies")]
    [InlineData("duplicate-property")]
    public async Task MalformedEnvelope_IsPermanentRejectionWithoutDurableRows(string mutation)
    {
        using var factory = await CreateFactoryAsync();
        var id = await SeedEventAsync(factory);
        string payload = "";
        await factory.WithDatabaseAsync(async db => payload = (await db.NotificationOutbox.SingleAsync()).Payload);
        var node = JsonNode.Parse(payload)!.AsObject();
        payload = mutation switch
        {
            "json" => "{broken",
            "message-id" => Change(node, "CorrelationId", Guid.NewGuid().ToString("N")),
            "unknown-kind" => Change(node, "Type", 99),
            "multiple-bodies" => Change(node, "ExecutedTask", JsonSerializer.SerializeToNode(new ExecutedTaskNotification(101, "name", DateTime.UtcNow))),
            _ => payload.Insert(1, "\"Type\":3,")
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => ProcessAsync(factory, id, payload));
        await AssertNoInboxAsync(factory);
    }

    [Theory]
    [InlineData("reassigned")]
    [InlineData("recipient-left")]
    [InlineData("project-moved")]
    [InlineData("task-deleted")]
    [InlineData("expired")]
    public async Task ObsoleteCreatedEvent_IsDurablySuppressedAndDoesNotResurrect(string reason)
    {
        var clock = new MutableAiClock();
        using var factory = await CreateFactoryAsync(clock);
        var id = await SeedEventAsync(factory);
        string? immutablePayload = null;
        await factory.WithDatabaseAsync(async db => immutablePayload = (await db.NotificationOutbox.SingleAsync(value => value.Id == id)).Payload);
        await factory.WithDatabaseAsync(async db =>
        {
            if (reason == "reassigned") (await db.Tasks.SingleAsync(value => value.Id == 101)).PerformerId = 102;
            if (reason == "recipient-left") (await db.Users.SingleAsync(value => value.Id == 101)).TeamId = 102;
            if (reason == "project-moved") (await db.Projects.SingleAsync(value => value.Id == 101)).TeamId = 102;
            if (reason == "task-deleted") db.Tasks.Remove(await db.Tasks.SingleAsync(value => value.Id == 101));
            await db.SaveChangesAsync();
        });
        if (reason == "expired") clock.Advance(TimeSpan.FromDays(91));
        var result = await ProcessAsync(factory, id, immutablePayload);
        Assert.False(result.Created);
        Assert.True(result.Suppressed);
        Assert.Null(result.RecipientId);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Empty(await db.Set<UserNotification>().ToListAsync());
            Assert.Equal(NotificationProcessingDisposition.Suppressed, (await db.Set<NotificationProcessingMarker>().SingleAsync()).Disposition);
            if (reason == "reassigned") (await db.Tasks.SingleAsync(value => value.Id == 101)).PerformerId = 101;
            await db.SaveChangesAsync();
        });
        Assert.True((await ProcessAsync(factory, id, immutablePayload)).Suppressed);
        await factory.WithDatabaseAsync(async db => Assert.Empty(await db.Set<UserNotification>().ToListAsync()));
    }

    [Theory]
    [InlineData(1, "occurrence")]
    [InlineData(1, "disabled")]
    [InlineData(1, "done")]
    [InlineData(1, "due-changed")]
    [InlineData(2, "occurrence")]
    [InlineData(2, "disabled")]
    public async Task StaleReminderOccurrence_CommitsSuppressedMarker(int kind, string reason)
    {
        using var factory = await CreateFactoryAsync();
        var id = await SeedEventAsync(factory, kind);
        await factory.WithDatabaseAsync(async db =>
        {
            var task = await db.Tasks.SingleAsync(value => value.Id == 101);
            if (reason == "occurrence")
            {
                if (kind == 1) task.ReminderNotificationId = Guid.NewGuid();
                else task.EscalationNotificationId = Guid.NewGuid();
            }
            if (reason == "disabled")
            {
                if (kind == 1) task.ReminderEnabled = false;
                else task.EscalationEnabled = false;
            }
            if (reason == "done") task.State = TaskState.Done;
            if (reason == "due-changed") task.DueDate = task.DueDate!.Value.AddDays(1);
            await db.SaveChangesAsync();
        });
        Assert.True((await ProcessAsync(factory, id)).Suppressed);
        await factory.WithDatabaseAsync(async db => Assert.Empty(await db.Set<UserNotification>().ToListAsync()));
    }

    [Fact]
    public async Task EscalationOnlyLegacyConfiguration_ProducesEscalationNotification()
    {
        using var factory = await CreateFactoryAsync();
        var id = await SeedEventAsync(factory, 2);
        await factory.WithDatabaseAsync(async db =>
        {
            (await db.Tasks.SingleAsync(value => value.Id == 101)).ReminderEnabled = false;
            await db.SaveChangesAsync();
        });
        var result = await ProcessAsync(factory, id);
        Assert.True(result.Created);
        Assert.False(result.Suppressed);
        await factory.WithDatabaseAsync(async db => Assert.Equal(2, (await db.UserNotifications.SingleAsync()).Kind));
    }

    [Fact]
    public async Task NotificationInsertFailure_RollsBackMarkerAndAllowsDeliveryRetry()
    {
        var fault = new InboxWriteFault();
        using var factory = await CreateFactoryAsync(interceptor: fault);
        var id = await SeedEventAsync(factory);
        fault.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => ProcessAsync(factory, id));
        Assert.Equal(1, fault.InjectedCount);
        await AssertNoInboxAsync(factory);
        Assert.True((await ProcessAsync(factory, id)).Created);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Single(await db.Set<UserNotification>().ToListAsync());
            Assert.Single(await db.Set<NotificationProcessingMarker>().ToListAsync());
        });
    }

    [Fact]
    public async Task Page_IsOwnOnlyCountsAllVisibleBeforeUnreadFilterAndUsesCurrentNames()
    {
        using var factory = await CreateFactoryAsync();
        await SeedInboxAsync(factory);
        using var client = factory.CreateUserClient(101);
        var page = await ReadPageAsync(client, "?page=1&pageSize=1&unreadOnly=true");
        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page.GetProperty("unreadCount").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(1, page.GetProperty("pageSize").GetInt32());
        var item = page.GetProperty("items")[0];
        Assert.Equal(item.GetProperty("id").GetGuid(), item.GetProperty("messageId").GetGuid());
        Assert.False(item.TryGetProperty("ownerId", out _));
        Assert.Equal("Team A task", item.GetProperty("taskName").GetString());
        Assert.Equal("Team A project", item.GetProperty("projectName").GetString());
        Assert.Equal(TimeSpan.Zero, item.GetProperty("occurredAtUtc").GetDateTimeOffset().Offset);
        var next = await ReadPageAsync(client, "?page=2&pageSize=1&unreadOnly=true");
        Assert.NotEqual(item.GetProperty("id").GetGuid(), next.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("actor-left")]
    [InlineData("project-moved")]
    [InlineData("task-moved")]
    [InlineData("reassigned")]
    [InlineData("task-deleted")]
    public async Task ScopeChanged_HidesRowsAndCountsAndReadReturns404(string change)
    {
        using var factory = await CreateFactoryAsync();
        var ids = await SeedInboxAsync(factory);
        await factory.WithDatabaseAsync(async db =>
        {
            if (change == "actor-left") (await db.Users.SingleAsync(value => value.Id == 101)).TeamId = 102;
            if (change == "project-moved") (await db.Projects.SingleAsync(value => value.Id == 101)).TeamId = 102;
            if (change == "task-moved") (await db.Tasks.SingleAsync(value => value.Id == 101)).ProjectId = 102;
            if (change == "reassigned") (await db.Tasks.SingleAsync(value => value.Id == 101)).PerformerId = 102;
            if (change == "task-deleted") db.Tasks.Remove(await db.Tasks.SingleAsync(value => value.Id == 101));
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateUserClient(101);
        var page = await ReadPageAsync(client);
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, page.GetProperty("unreadCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync($"/api/notifications/{ids[0]}/read", null)).StatusCode);
    }

    [Theory]
    [InlineData(102, false)]
    [InlineData(105, true)]
    public async Task ForeignOwnerIncludingAdministrator_CannotReadOrCountAnotherInbox(int userId, bool admin)
    {
        using var factory = await CreateFactoryAsync();
        var ids = await SeedInboxAsync(factory);
        using var client = factory.CreateUserClient(userId, admin);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync($"/api/notifications/{ids[0]}/read", null)).StatusCode);
        var page = await ReadPageAsync(client);
        Assert.DoesNotContain(page.GetProperty("items").EnumerateArray(), value => value.GetProperty("id").GetGuid() == ids[0]);
        Assert.Equal(userId == 102 ? 1 : 0, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task MarkRead_IsIdempotentWithFirstUtcTimestampAndCountsRemainConsistent()
    {
        var clock = new MutableAiClock();
        using var factory = await CreateFactoryAsync(clock);
        var ids = await SeedInboxAsync(factory);
        using var client = factory.CreateUserClient(101);
        var first = await client.PutAsync($"/api/notifications/{ids[0]}/read", null);
        first.EnsureSuccessStatusCode();
        var readAt = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement.GetProperty("readAtUtc").GetDateTimeOffset();
        Assert.Equal(clock.GetUtcNow(), readAt);
        clock.Advance(TimeSpan.FromHours(2));
        var again = await client.PutAsync($"/api/notifications/{ids[0]}/read", null);
        again.EnsureSuccessStatusCode();
        Assert.Equal(readAt, JsonDocument.Parse(await again.Content.ReadAsStringAsync()).RootElement.GetProperty("readAtUtc").GetDateTimeOffset());
        var page = await ReadPageAsync(client, "?unreadOnly=true");
        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, page.GetProperty("unreadCount").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task RecipientRevokedAtNotificationWrite_FinalGuardSuppressesWithoutDelivering()
    {
        var fault = new MembershipBoundaryFault("INSERT INTO \"UserNotifications\"");
        using var factory = await CreateFactoryAsync(interceptor: fault);
        var id = await SeedEventAsync(factory);
        fault.Armed = true;
        var result = await ProcessAsync(factory, id);
        Assert.Equal(1, fault.InjectedCount);
        Assert.True(result.Suppressed);
        Assert.False(result.Created);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Empty(await db.Set<UserNotification>().ToListAsync());
            Assert.Equal(NotificationProcessingDisposition.Suppressed, (await db.Set<NotificationProcessingMarker>().SingleAsync()).Disposition);
        });
    }

    [Fact]
    public async Task CallerRevokedAtReadWrite_FinalGuardReturns404AndRollsBackReadTimestamp()
    {
        var fault = new MembershipBoundaryFault("UPDATE \"UserNotifications\"");
        using var factory = await CreateFactoryAsync(interceptor: fault);
        var ids = await SeedInboxAsync(factory);
        using var client = factory.CreateUserClient(101);
        fault.Armed = true;
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync($"/api/notifications/{ids[0]}/read", null)).StatusCode);
        Assert.Equal(1, fault.InjectedCount);
        await factory.WithDatabaseAsync(async db => Assert.Null((await db.Set<UserNotification>().SingleAsync(value => value.Id == ids[0])).ReadAtUtc));
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=100001")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task InvalidPaging_Returns400(string query)
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateUserClient(101);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/notifications" + query)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_InboxAndReadRequireAuthentication()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateUserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsync($"/api/notifications/{Guid.NewGuid()}/read", null)).StatusCode);
    }

    [Fact]
    public async Task NotificationRetention_PurgesOldContentInBoundedBatchButKeepsDedupMarkers()
    {
        var clock = new MutableAiClock();
        using var factory = await CreateFactoryAsync(clock);
        var id = await SeedEventAsync(factory);
        await ProcessAsync(factory, id);
        clock.Advance(TimeSpan.FromDays(91));
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<NotificationRetentionService>().CleanupExpiredAsync());
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Empty(await db.Set<UserNotification>().ToListAsync());
            Assert.Single(await db.Set<NotificationProcessingMarker>().ToListAsync());
        });
        Assert.False((await ProcessAsync(factory, id)).Created);
        await factory.WithDatabaseAsync(async db => Assert.Empty(await db.Set<UserNotification>().ToListAsync()));
    }

    [Fact]
    public async Task Retention_BatchesAtMost100OldNotificationsAndPreservesRecentContentAndMarkers()
    {
        var clock = new MutableAiClock();
        using var factory = await CreateFactoryAsync(clock);
        await factory.WithDatabaseAsync(async db =>
        {
            for (var index = 0; index < 102; index++)
            {
                var id = Guid.NewGuid();
                var time = clock.GetUtcNow().UtcDateTime.AddDays(index == 101 ? -1 : -91);
                db.Add(new UserNotification { Id = id, OwnerId = 101, Kind = 3, TaskId = 101, ProjectId = 101,
                    TaskName = "Task", ProjectName = "Project", OccurredAtUtc = time });
                db.Add(new NotificationProcessingMarker { MessageId = id, CanonicalPayloadHash = new string('A', 64),
                    ProcessedAtUtc = time, Disposition = NotificationProcessingDisposition.Delivered });
            }
            await db.SaveChangesAsync();
        });
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(100, await scope.ServiceProvider.GetRequiredService<NotificationRetentionService>().CleanupExpiredAsync());
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Equal(2, await db.Set<UserNotification>().CountAsync());
            Assert.Equal(102, await db.Set<NotificationProcessingMarker>().CountAsync());
            Assert.True(await db.Set<UserNotification>().AnyAsync(value => value.OccurredAtUtc > clock.GetUtcNow().UtcDateTime.AddDays(-90)));
        });
    }

    [Fact]
    public async Task AiRetention_ResolvesWithoutInferenceProviderAndErasesExpiredPrivateSource()
    {
        var clock = new MutableAiClock();
        using var factory = await CreateFactoryAsync(clock);
        await factory.WithDatabaseAsync(async db =>
        {
            var draft = new AiTaskDraft { Id = Guid.NewGuid(), Version = Guid.NewGuid(), OwnerId = 101, ProjectId = 101,
                NotesText = "Private notes", Locale = "uk", TimeZoneId = "Europe/Kyiv", ReferenceDate = new DateOnly(2026, 10, 12),
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime.AddDays(-2), ExpiresAtUtc = clock.GetUtcNow().UtcDateTime.AddDays(-1),
                RetainUntilUtc = clock.GetUtcNow().UtcDateTime.AddDays(5) };
            draft.Items.Add(new AiTaskDraftItem { Id = Guid.NewGuid(), DraftId = draft.Id, CandidateJson = "private evidence", Ordinal = 0 });
            db.Add(draft);
            await db.SaveChangesAsync();
        });
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IAiDraftRetentionService>().CleanupExpiredAsync());
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Null((await db.Set<AiTaskDraft>().SingleAsync()).NotesText);
            Assert.Null((await db.Set<AiTaskDraftItem>().SingleAsync()).CandidateJson);
            Assert.Equal(4, await db.Tasks.CountAsync());
        });
    }

    [Fact]
    public async Task AiRetention_ProcessesAtMost100DraftsAndDoesNotPurgeBusinessRecords()
    {
        var clock = new MutableAiClock();
        using var factory = await CreateFactoryAsync(clock);
        await factory.WithDatabaseAsync(async db =>
        {
            for (var index = 0; index < 101; index++)
                db.Add(new AiTaskDraft { Id = Guid.NewGuid(), Version = Guid.NewGuid(), OwnerId = 101, ProjectId = 101,
                    NotesText = "Private source", Locale = "uk", TimeZoneId = "Europe/Kyiv", ReferenceDate = new DateOnly(2026, 10, 12),
                    CreatedAtUtc = clock.GetUtcNow().UtcDateTime.AddDays(-2), ExpiresAtUtc = clock.GetUtcNow().UtcDateTime.AddDays(-1),
                    RetainUntilUtc = clock.GetUtcNow().UtcDateTime.AddDays(5) });
            await db.SaveChangesAsync();
        });
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(100, await scope.ServiceProvider.GetRequiredService<IAiDraftRetentionService>().CleanupExpiredAsync());
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Equal(101, await db.Set<AiTaskDraft>().CountAsync());
            Assert.Equal(1, await db.Set<AiTaskDraft>().CountAsync(value => value.NotesText != null));
            Assert.Equal(4, await db.Tasks.CountAsync());
            Assert.Equal(4, await db.Projects.CountAsync());
        });
    }

    internal static Task<DataIsolationFactory> CreateFactoryAsync(MutableAiClock? clock = null, IInterceptor? interceptor = null)
        => DataIsolationFactory.CreateAsync(interceptor: interceptor, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock ?? new MutableAiClock());
            services.RemoveAll<INotificationInboxService>();
            services.AddScoped<INotificationInboxService, NotificationInboxService>();
            services.RemoveAll<INotificationMessageProcessor>();
            services.AddScoped<INotificationMessageProcessor, NotificationMessageProcessor>();
            services.RemoveAll<IAiDraftRetentionService>();
            services.AddScoped<IAiDraftRetentionService, AiDraftRetentionService>();
            services.AddScoped<NotificationRetentionService>();
            services.RemoveAll<ITaskExtractionProvider>();
            services.AddScoped<ITaskExtractionProvider>(_ => throw new InvalidOperationException("Retention must not resolve inference."));
        });

    internal static async Task<Guid> SeedEventAsync(DataIsolationFactory factory, int kind = 3)
    {
        var id = Guid.NewGuid();
        var now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        await factory.WithDatabaseAsync(async db =>
        {
            var task = await db.Tasks.SingleAsync(value => value.Id == 101);
            task.CreatedAt = now;
            var envelope = new TaskNotificationEnvelope { Type = (NotificationEventType)kind, CorrelationId = id.ToString("N") };
            if (kind == 3) envelope.TaskCreated = new(id, task.Id, "Untrusted event title", task.PerformerId, task.ProjectId, "Untrusted project", now);
            if (kind is 1 or 2)
            {
                task.DueDate = now.AddMinutes(-60); task.ReminderEnabled = true; task.ReminderOffsetMinutes = 15;
                task.EscalationEnabled = true; task.EscalationDelayMinutes = 30;
                if (kind == 1) task.ReminderNotificationId = id;
                else task.EscalationNotificationId = id;
                envelope.Reminder = new(task.Id, "Untrusted event title", task.PerformerId, task.ProjectId, "Untrusted project", task.DueDate.Value, 15, 30);
            }
            if (kind == 0)
            {
                task.State = TaskState.Done; task.FinishedAt = now;
                db.ExecutedTasks.Add(new ExecutedTask { TaskId = task.Id, TaskName = task.Name, CreatedAt = now, NotificationMessageId = id });
                envelope.ExecutedTask = new(task.Id, "Untrusted event title", now, task.PerformerId, task.ProjectId, "Untrusted project");
            }
            var payload = JsonSerializer.SerializeToNode(envelope)!;
            db.NotificationOutbox.Add(new NotificationOutboxMessage { Id = id, TaskId = task.Id,
                Kind = kind == 3 ? NotificationOutboxKind.TaskCreated : kind == 0 ? NotificationOutboxKind.ExecutedTaskCompleted : NotificationOutboxKind.Reminder,
                IsEscalation = kind == 2, QueueName = "notification-tests", Payload = payload.ToJsonString(), CreatedAtUtc = now });
            await db.SaveChangesAsync();
        });
        return id;
    }

    internal static async Task<NotificationProcessingResult> ProcessAsync(DataIsolationFactory factory, Guid id, string? payload = null)
    {
        if (payload is null) await factory.WithDatabaseAsync(async db => payload = (await db.NotificationOutbox.SingleAsync(value => value.Id == id)).Payload);
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationMessageProcessor>().ProcessAsync(id, payload!);
    }

    private static async Task<Guid[]> SeedInboxAsync(DataIsolationFactory factory)
    {
        var now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        await factory.WithDatabaseAsync(async db =>
        {
            for (var index = 0; index < 5; index++)
            {
                db.Set<NotificationProcessingMarker>().Add(new NotificationProcessingMarker { MessageId = ids[index],
                    CanonicalPayloadHash = new string('A', 64), ProcessedAtUtc = now.AddMinutes(-index),
                    Disposition = NotificationProcessingDisposition.Delivered });
                db.Set<UserNotification>().Add(new UserNotification { Id = ids[index], OwnerId = index == 3 ? 102 : 101,
                    TaskId = index == 3 ? 103 : index == 4 ? 102 : 101, ProjectId = index == 4 ? 102 : 101,
                    Kind = 3, TaskName = "Old task name", ProjectName = "Old project name", OccurredAtUtc = now.AddMinutes(-index),
                    ReadAtUtc = index == 2 ? now.AddHours(-1) : null });
            }
            await db.SaveChangesAsync();
        });
        return ids;
    }

    private static async Task<JsonElement> ReadPageAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/notifications" + query);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task AssertNoInboxAsync(DataIsolationFactory factory) => await factory.WithDatabaseAsync(async db =>
    {
        Assert.Empty(await db.Set<UserNotification>().ToListAsync());
        Assert.Empty(await db.Set<NotificationProcessingMarker>().ToListAsync());
    });

    private static string Change<T>(JsonObject node, string property, T value)
    {
        node[property] = JsonSerializer.SerializeToNode(value);
        return node.ToJsonString();
    }

    private sealed class InboxWriteFault : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public int InjectedCount { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
        private void Inspect(DbCommand command)
        {
            if (!Armed || !command.CommandText.Contains("INSERT INTO \"UserNotifications\"", StringComparison.Ordinal)) return;
            Armed = false; InjectedCount++;
            throw new IOException("Synthetic inbox transaction write failure.");
        }
    }

    // Same-connection mutation proves final-write guards; actual concurrent
    // SQL Server locking is exercised by a separate disposable-database gate.
    private sealed class MembershipBoundaryFault(string commandPrefix) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public int InjectedCount { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
        private void Inspect(DbCommand command)
        {
            if (!Armed || !command.CommandText.Contains(commandPrefix, StringComparison.Ordinal)) return;
            Armed = false; InjectedCount++;
            using var revoke = command.Connection!.CreateCommand();
            revoke.Transaction = command.Transaction;
            revoke.CommandText = "UPDATE \"AspNetUsers\" SET \"TeamId\"=102 WHERE \"Id\"=101";
            Assert.Equal(1, revoke.ExecuteNonQuery());
        }
    }
}
