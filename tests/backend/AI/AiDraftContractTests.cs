using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using BLL.Interfaces;
using BLL.Models.AI;
using BLL.Models.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Integration;
using Xunit;

namespace Tests.AI;

public sealed class AiDraftContractTests
{
    internal const string Notes = "Олена завтра перевірить API оплати. Сергій до 2026-10-16 підготує міграцію.";

    [Fact]
    public async Task Anonymous_AiRoutesRequireAuthentication()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateUserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/ai/task-extractions", Extraction())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/ai/task-extractions/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task DisabledAi_ReportsUnavailable_AndManualTaskCreationWorks()
    {
        using var factory = await CreateFactoryAsync(provider: new FixtureExtractionProvider(available: false));
        using var client = factory.CreateUserClient(101);
        var statusResponse = await client.GetAsync("/api/ai/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(status.GetProperty("available").GetBoolean());
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/ai/task-extractions", Extraction()), HttpStatusCode.ServiceUnavailable);
        var manual = await client.PostAsJsonAsync("/api/tasks", new
        {
            projectId = 101, performerId = 101, name = "Manual task while AI is unavailable", description = "Manual workflow", state = 0
        });
        Assert.Equal(HttpStatusCode.OK, manual.StatusCode);
    }

    [Fact]
    public async Task Extraction_PersistsPrivateVersionedDraftWithoutTasksOutboxOrBrokerCalls()
    {
        var provider = new FixtureExtractionProvider();
        var queue = new CountingQueue();
        using var factory = await CreateFactoryAsync(provider, queue: queue);
        using var client = factory.CreateUserClient(101);
        var before = await BusinessSnapshotAsync(factory);
        var draft = await CreateDraftAsync(client);
        Assert.Equal(101, draft.GetProperty("ownerId").GetInt32());
        Assert.Equal(101, draft.GetProperty("projectId").GetInt32());
        Assert.NotEqual(Guid.Empty, draft.GetProperty("version").GetGuid());
        Assert.Equal("2026-10-12", draft.GetProperty("referenceDate").GetString());
        Assert.Equal("Europe/Kyiv", draft.GetProperty("timeZoneId").GetString());
        Assert.Equal(2, draft.GetProperty("items").GetArrayLength());
        var ids = draft.GetProperty("items").EnumerateArray().Select(value => value.GetProperty("draftItemId").GetGuid()).ToArray();
        Assert.Equal(2, ids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.Equal(before, await BusinessSnapshotAsync(factory));
        Assert.Equal(0, queue.Calls);
        Assert.NotNull(provider.LastInput);
        Assert.Equal(new[] { 101, 102 }, provider.LastInput!.AllowedAssignees.Select(value => value.Id).Order().ToArray());
        Assert.DoesNotContain(provider.LastInput.AllowedAssignees, value => value.Id == 103);
        var reloaded = await client.GetFromJsonAsync<JsonElement>($"/api/ai/task-extractions/{draft.GetProperty("id").GetGuid()}");
        Assert.Equal(draft.GetRawText(), reloaded.GetRawText());
    }

    [Fact]
    public async Task ForeignProject_IsRejectedBeforeProviderInvocation()
    {
        var provider = new FixtureExtractionProvider();
        using var factory = await CreateFactoryAsync(provider);
        using var client = factory.CreateUserClient(101);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/ai/task-extractions", Extraction(projectId: 102)), HttpStatusCode.NotFound);
        Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData(102, false)]
    [InlineData(103, false)]
    [InlineData(105, true)]
    public async Task OtherUserIncludingGlobalAdmin_CannotReadCommitOrEraseAuthorsDraft(int actorId, bool admin)
    {
        using var factory = await CreateFactoryAsync();
        using var author = factory.CreateUserClient(101);
        var draft = await CreateDraftAsync(author);
        using var stranger = factory.CreateUserClient(actorId, admin);
        var id = draft.GetProperty("id").GetGuid();
        await AssertProblemAsync(await stranger.GetAsync($"/api/ai/task-extractions/{id}"), HttpStatusCode.NotFound);
        await AssertProblemAsync(await CommitAsync(stranger, draft, [Item(draft, 0)], "foreign-receipt"), HttpStatusCode.NotFound);
        await AssertProblemAsync(await stranger.DeleteAsync($"/api/ai/task-extractions/{id}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokedAuthor_CannotReadOrCommit_ButCanErasePrivateDraft()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateUserClient(101);
        var draft = await CreateDraftAsync(client);
        await factory.WithDatabaseAsync(async db =>
        {
            (await db.Users.SingleAsync(value => value.Id == 101)).TeamId = 102;
            await db.SaveChangesAsync();
        });
        var before = await BusinessSnapshotAsync(factory);
        var path = $"/api/ai/task-extractions/{draft.GetProperty("id").GetGuid()}";
        await AssertProblemAsync(await client.GetAsync(path), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await CommitAsync(client, draft, [Item(draft, 0)], "revoked"), HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path)).StatusCode);
        Assert.Equal(before, await BusinessSnapshotAsync(factory));
    }

    [Fact]
    public async Task MembershipRevokedDuringInference_IsRecheckedBeforeDraftPersistence()
    {
        var provider = new FixtureExtractionProvider();
        using var factory = await CreateFactoryAsync(provider);
        provider.BeforeReturn = async _ => await factory.WithDatabaseAsync(async db =>
        {
            (await db.Users.SingleAsync(value => value.Id == 101)).TeamId = 102;
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateUserClient(101);
        var before = await BusinessSnapshotAsync(factory);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/ai/task-extractions", Extraction()), HttpStatusCode.Forbidden);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(before, await BusinessSnapshotAsync(factory));
    }

    [Fact]
    public async Task Tomorrow_UsesFrozenLocalReferenceDateAtUtcBoundary()
    {
        var clock = new MutableAiClock(new DateTimeOffset(2026, 10, 12, 21, 30, 0, TimeSpan.Zero));
        using var factory = await CreateFactoryAsync(clock: clock);
        using var client = factory.CreateUserClient(101);
        var response = await client.PostAsJsonAsync("/api/ai/task-extractions", new { notesText = Notes, projectId = 101, timeZoneId = "Europe/Kyiv", locale = "uk" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-10-13", draft.GetProperty("referenceDate").GetString());
        var item = draft.GetProperty("items")[0];
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 20, 59, 0, TimeSpan.Zero), item.GetProperty("proposedDueAtUtc").GetDateTimeOffset());
        Assert.Contains("date_only_end_of_day", item.GetProperty("interpretations").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task AmbiguousAndUnknownAssignees_RemainUnresolvedDespiteModelId()
    {
        using var factory = await CreateFactoryAsync();
        await factory.WithDatabaseAsync(async db =>
        {
            var peer = await db.Users.SingleAsync(value => value.Id == 102);
            peer.FirstName = "Олена";
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateUserClient(101);
        var draft = await CreateDraftAsync(client);
        var ambiguous = draft.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, NullableProperty(ambiguous, "proposedAssigneeId").ValueKind);
        Assert.NotEmpty(ambiguous.GetProperty("clarificationReasons").EnumerateArray());
        // The source names Сергій, but the raw model supplied a valid team ID.
        // The caller's team contains no uniquely matching Сергій after the rename.
        Assert.Equal(JsonValueKind.Null, NullableProperty(draft.GetProperty("items")[1], "proposedAssigneeId").ValueKind);
    }

    [Fact]
    public async Task PartialCommit_UsesEditsAndPreservesRemainingItems_RotatesDraftVersion()
    {
        var queue = new CountingQueue();
        using var factory = await CreateFactoryAsync(queue: queue);
        using var client = factory.CreateUserClient(101);
        var original = await CreateDraftAsync(client);
        var before = await BusinessSnapshotAsync(factory);
        var response = await CommitAsync(client, original, [Item(original, 0, title: "User edited title", assigneeId: 102)], "first-selection");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>();
        var committed = receipt.GetProperty("items")[0];
        Assert.NotEqual(Guid.Empty, committed.GetProperty("taskVersion").GetGuid());
        Assert.NotEqual(Guid.Empty, committed.GetProperty("outboxMessageId").GetGuid());
        Assert.NotEqual(original.GetProperty("version").GetGuid(), receipt.GetProperty("draftVersion").GetGuid());
        await factory.WithDatabaseAsync(async db =>
        {
            var created = await db.Tasks.SingleAsync(value => value.Id == committed.GetProperty("taskId").GetInt32());
            Assert.Equal("User edited title", created.Name);
            Assert.Equal(102, created.PerformerId);
            Assert.Equal(101, created.ProjectId);
            var outbox = await db.NotificationOutbox.SingleAsync(value => value.Id == committed.GetProperty("outboxMessageId").GetGuid());
            Assert.Equal(created.Id, outbox.TaskId);
            Assert.Null(outbox.PublishedAtUtc);
            Assert.Equal(3, (int)JsonSerializer.Deserialize<TaskNotificationEnvelope>(outbox.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Type);
            Assert.DoesNotContain("notesText", outbox.Payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sourceQuote", outbox.Payload, StringComparison.OrdinalIgnoreCase);
        });
        var current = await client.GetFromJsonAsync<JsonElement>($"/api/ai/task-extractions/{original.GetProperty("id").GetGuid()}");
        Assert.Equal(committed.GetProperty("taskId").GetInt32(), current.GetProperty("items")[0].GetProperty("materializedTaskId").GetInt32());
        Assert.Equal(JsonValueKind.Null, NullableProperty(current.GetProperty("items")[1], "materializedTaskId").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await CommitAsync(client, current, [Item(current, 1)], "remaining-selection")).StatusCode);
        var after = await BusinessSnapshotAsync(factory);
        Assert.Equal(before.Tasks + 2, after.Tasks);
        Assert.Equal(before.Outbox + 2, after.Outbox);
        Assert.Equal(0, queue.Calls);
    }

    [Fact]
    public async Task SameKeySameTypedPayload_ReturnsExactReceiptBeforeStaleVersionCheck()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateUserClient(101);
        var draft = await CreateDraftAsync(client);
        var items = new[] { Item(draft, 0) };
        var first = await CommitAsync(client, draft, items, "stable-receipt");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var originalReceipt = await first.Content.ReadAsStringAsync();
        var beforeReplay = await BusinessSnapshotAsync(factory);
        var replay = await CommitAsync(client, draft, items, "stable-receipt");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(originalReceipt, await replay.Content.ReadAsStringAsync());
        Assert.Equal(beforeReplay, await BusinessSnapshotAsync(factory));
    }

    [Fact]
    public async Task ReplayCanonicalizesPropertyAndSelectionOrder_WithoutAnotherWrite()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateUserClient(101);
        var draft = await CreateDraftAsync(client);
        var first = await CommitAsync(client, draft, [Item(draft, 0), Item(draft, 1)], "canonical-key");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var receipt = await first.Content.ReadAsStringAsync();
        var before = await BusinessSnapshotAsync(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/task-extractions/{draft.GetProperty("id").GetGuid()}/commit");
        request.Headers.Add("Idempotency-Key", "canonical-key");
        request.Content = JsonContent.Create(new
        {
            items = new[] { Item(draft, 1), Item(draft, 0) },
            expectedVersion = draft.GetProperty("version").GetGuid().ToString("D").ToUpperInvariant()
        });
        var replay = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(receipt, await replay.Content.ReadAsStringAsync());
        Assert.Equal(before, await BusinessSnapshotAsync(factory));
    }

    [Fact]
    public async Task SuccessfulReceiptReplay_StillRequiresFreshProjectAccess()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateUserClient(101);
        var draft = await CreateDraftAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await CommitAsync(client, draft, [Item(draft, 0)], "revoked-replay")).StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            (await db.Users.SingleAsync(value => value.Id == 101)).TeamId = 102;
            await db.SaveChangesAsync();
        });
        var before = await BusinessSnapshotAsync(factory);
        await AssertProblemAsync(await CommitAsync(client, draft, [Item(draft, 0)], "revoked-replay"), HttpStatusCode.Forbidden);
        Assert.Equal(before, await BusinessSnapshotAsync(factory));
    }

    [Fact]
    public async Task SameKeyDifferentPayload_ConflictsWithoutAnotherTask()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateUserClient(101);
        var draft = await CreateDraftAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await CommitAsync(client, draft, [Item(draft, 0)], "same-key")).StatusCode);
        var before = await BusinessSnapshotAsync(factory);
        await AssertProblemAsync(await CommitAsync(client, draft, [Item(draft, 0, title: "Changed replay")], "same-key"), HttpStatusCode.Conflict);
        Assert.Equal(before, await BusinessSnapshotAsync(factory));
    }

    [Fact]
    public async Task NewKeyCannotRematerializeSelectedItem_EvenAfterTaskDeleted()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateUserClient(101);
        var draft = await CreateDraftAsync(client);
        var first = await CommitAsync(client, draft, [Item(draft, 0)], "materialized");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var receipt = await first.Content.ReadFromJsonAsync<JsonElement>();
        var taskId = receipt.GetProperty("items")[0].GetProperty("taskId").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/tasks/{taskId}")).StatusCode);
        var current = await client.GetFromJsonAsync<JsonElement>($"/api/ai/task-extractions/{draft.GetProperty("id").GetGuid()}");
        var before = await BusinessSnapshotAsync(factory);
        await AssertProblemAsync(await CommitAsync(client, current, [Item(current, 0)], "second-key"), HttpStatusCode.Conflict);
        Assert.Equal(before, await BusinessSnapshotAsync(factory));
    }

    [Theory]
    [InlineData("missing-version")]
    [InlineData("stale-version")]
    [InlineData("missing-key")]
    [InlineData("foreign-assignee")]
    [InlineData("duplicate-item")]
    public async Task InvalidCommit_HasNoTaskOutboxOrVersionSideEffects(string failure)
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateUserClient(101);
        var draft = await CreateDraftAsync(client);
        var before = await BusinessSnapshotAsync(factory);
        Guid? expected = failure == "missing-version" ? null : failure == "stale-version" ? Guid.NewGuid() : draft.GetProperty("version").GetGuid();
        var item = Item(draft, 0, assigneeId: failure == "foreign-assignee" ? 103 : 101);
        var items = failure == "duplicate-item" ? new[] { item, item } : new[] { item };
        var response = await CommitAsync(client, draft, items, failure == "missing-key" ? null : "invalid", expected,
            omitVersion: failure == "missing-version");
        var expectedStatus = failure == "missing-version" ? (HttpStatusCode)428 : failure == "stale-version" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest;
        await AssertProblemAsync(response, expectedStatus);
        Assert.Equal(before, await BusinessSnapshotAsync(factory));
        var current = await client.GetFromJsonAsync<JsonElement>($"/api/ai/task-extractions/{draft.GetProperty("id").GetGuid()}");
        Assert.Equal(draft.GetProperty("version").GetGuid(), current.GetProperty("version").GetGuid());
    }

    [Fact]
    public async Task InvalidSecondItem_RollsBackEntireSelection_AndKeyCanBeUsedAfterCorrection()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateUserClient(101);
        var draft = await CreateDraftAsync(client);
        var before = await BusinessSnapshotAsync(factory);
        await AssertProblemAsync(await CommitAsync(client, draft, [Item(draft, 0), Item(draft, 1, assigneeId: 103)], "all-or-nothing"), HttpStatusCode.BadRequest);
        Assert.Equal(before, await BusinessSnapshotAsync(factory));
        Assert.Equal(HttpStatusCode.OK, (await CommitAsync(client, draft, [Item(draft, 0), Item(draft, 1)], "all-or-nothing")).StatusCode);
    }

    [Fact]
    public async Task OutboxInsertFailure_RollsBackTasksMaterializationsReceiptAndDraftVersion()
    {
        var failure = new AiOutboxFailureInterceptor();
        using var factory = await CreateFactoryAsync(interceptor: failure);
        using var client = factory.CreateUserClient(101);
        var draft = await CreateDraftAsync(client);
        var before = await BusinessSnapshotAsync(factory);
        failure.Enabled = true;
        await AssertProblemAsync(await CommitAsync(client, draft, [Item(draft, 0), Item(draft, 1)], "late-failure"), HttpStatusCode.Conflict);
        Assert.True(failure.Triggered);
        failure.Enabled = false;
        Assert.Equal(before, await BusinessSnapshotAsync(factory));
        var reloaded = await client.GetFromJsonAsync<JsonElement>($"/api/ai/task-extractions/{draft.GetProperty("id").GetGuid()}");
        Assert.Equal(draft.GetProperty("version").GetGuid(), reloaded.GetProperty("version").GetGuid());
        Assert.All(reloaded.GetProperty("items").EnumerateArray(), value => Assert.Equal(JsonValueKind.Null, NullableProperty(value, "materializedTaskId").ValueKind));
        Assert.Equal(HttpStatusCode.OK, (await CommitAsync(client, draft, [Item(draft, 0), Item(draft, 1)], "late-failure")).StatusCode);
    }

    [Fact]
    public async Task ExpiredDraft_RejectsNewCommit_ButSuccessfulReceiptSurvivesUntilReplayExpiry()
    {
        var clock = new MutableAiClock();
        using var factory = await CreateFactoryAsync(clock: clock);
        using var client = factory.CreateUserClient(101);
        var committedDraft = await CreateDraftAsync(client);
        var pendingDraft = await CreateDraftAsync(client);
        var response = await CommitAsync(client, committedDraft, [Item(committedDraft, 0)], "recoverable");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = await response.Content.ReadAsStringAsync();
        clock.Advance(TimeSpan.FromHours(25));
        await AssertProblemAsync(await CommitAsync(client, pendingDraft, [Item(pendingDraft, 0)], "expired-new"), HttpStatusCode.Gone);
        var replay = await CommitAsync(client, committedDraft, [Item(committedDraft, 0)], "recoverable");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(receipt, await replay.Content.ReadAsStringAsync());
        clock.Advance(TimeSpan.FromDays(7));
        await AssertProblemAsync(await CommitAsync(client, committedDraft, [Item(committedDraft, 0)], "recoverable"), HttpStatusCode.Gone);
    }

    [Fact]
    public async Task DeletedDraft_ErasesSourceButPreservesBusinessTasksAndRecoverableReceipt()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateUserClient(101);
        var draft = await CreateDraftAsync(client);
        var first = await CommitAsync(client, draft, [Item(draft, 0)], "erased-source");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var receipt = await first.Content.ReadAsStringAsync();
        var before = await BusinessSnapshotAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/ai/task-extractions/{draft.GetProperty("id").GetGuid()}")).StatusCode);
        await AssertProblemAsync(await client.GetAsync($"/api/ai/task-extractions/{draft.GetProperty("id").GetGuid()}"), HttpStatusCode.Gone);
        var replay = await CommitAsync(client, draft, [Item(draft, 0)], "erased-source");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(receipt, await replay.Content.ReadAsStringAsync());
        Assert.Equal(before, await BusinessSnapshotAsync(factory));
    }

    [Fact]
    public async Task ProductDailyBudget_BlocksSecondUserBeforeInferenceAcrossRequests()
    {
        var provider = new FixtureExtractionProvider { ReservationTokens = 500 };
        using var factory = await CreateFactoryAsync(provider, configuration: new Dictionary<string, string?>
        {
            ["AI:UserDailyTokenBudget"] = "1000", ["AI:ProductDailyTokenBudget"] = "750"
        });
        using var first = factory.CreateUserClient(101);
        using var second = factory.CreateUserClient(102);
        await CreateDraftAsync(first);
        await AssertProblemAsync(await second.PostAsJsonAsync("/api/ai/task-extractions", Extraction()), HttpStatusCode.TooManyRequests);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ProviderFailure_DoesNotWriteBusinessData_AndUnknownUsageReservationIsKept()
    {
        var provider = new FixtureExtractionProvider { ReservationTokens = 500, Failure = new TaskCanceledException("Synthetic timeout") };
        using var factory = await CreateFactoryAsync(provider, configuration: new Dictionary<string, string?>
        {
            ["AI:UserDailyTokenBudget"] = "750", ["AI:ProductDailyTokenBudget"] = "1000"
        });
        using var client = factory.CreateUserClient(101);
        var before = await BusinessSnapshotAsync(factory);
        var failed = await client.PostAsJsonAsync("/api/ai/task-extractions", Extraction());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(before, await BusinessSnapshotAsync(factory));
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/ai/task-extractions", Extraction()), HttpStatusCode.TooManyRequests);
        Assert.Equal(1, provider.Calls);
    }

    internal static object Extraction(int projectId = 101) => new
    {
        notesText = Notes, projectId, noteDate = "2026-10-12", timeZoneId = "Europe/Kyiv", locale = "uk"
    };

    // The application's established JSON settings omit null properties. Both
    // omitted and explicit-null proposals represent the same unresolved value.
    internal static JsonElement NullableProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : JsonSerializer.SerializeToElement((object?)null);

    internal static async Task<JsonElement> CreateDraftAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/ai/task-extractions", Extraction());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal static object Item(JsonElement draft, int index, string? title = null, int assigneeId = 101) => new
    {
        draftItemId = draft.GetProperty("items")[index].GetProperty("draftItemId").GetGuid(),
        title = title ?? draft.GetProperty("items")[index].GetProperty("title").GetString(), description = "Confirmed by the user",
        assigneeId, dueAtUtc = (DateTimeOffset?)null, reminderEnabled = false, reminderOffsetMinutes = (int?)null,
        escalationEnabled = false, escalationDelayMinutes = (int?)null
    };

    internal static async Task<HttpResponseMessage> CommitAsync(HttpClient client, JsonElement draft, object[] items, string? key,
        Guid? expectedVersion = null, bool omitVersion = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/task-extractions/{draft.GetProperty("id").GetGuid()}/commit");
        Guid? version = omitVersion ? null : expectedVersion ?? draft.GetProperty("version").GetGuid();
        request.Content = JsonContent.Create(new { expectedVersion = version, items });
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    internal static async Task<DataIsolationFactory> CreateFactoryAsync(FixtureExtractionProvider? provider = null,
        MutableAiClock? clock = null, CountingQueue? queue = null,
        IReadOnlyDictionary<string, string?>? configuration = null, IInterceptor? interceptor = null)
    {
        provider ??= new FixtureExtractionProvider();
        clock ??= new MutableAiClock();
        var factory = await DataIsolationFactory.CreateAsync(interceptor: interceptor, additionalConfiguration: configuration,
            configureServices: services =>
            {
                services.RemoveAll<ITaskExtractionProvider>();
                services.AddSingleton<ITaskExtractionProvider>(provider);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                if (queue is not null)
                {
                    services.RemoveAll<IQueueService>();
                    services.AddSingleton<IQueueService>(queue);
                }
            });
        await factory.WithDatabaseAsync(async db =>
        {
            var first = await db.Users.SingleAsync(value => value.Id == 101);
            first.FirstName = "Олена"; first.LastName = "Коваль";
            var second = await db.Users.SingleAsync(value => value.Id == 102);
            second.FirstName = "Сергій"; second.LastName = "Мельник";
            await db.SaveChangesAsync();
        });
        return factory;
    }

    internal static async Task<(int Tasks, int Outbox, string Versions)> BusinessSnapshotAsync(DataIsolationFactory factory)
    {
        (int Tasks, int Outbox, string Versions) snapshot = default;
        await factory.WithDatabaseAsync(async db =>
        {
            var tasks = await db.Tasks.AsNoTracking().OrderBy(value => value.Id).Select(value => new { value.Id, value.Version, value.Name }).ToListAsync();
            snapshot = (tasks.Count, await db.NotificationOutbox.CountAsync(), JsonSerializer.Serialize(tasks));
        });
        return snapshot;
    }

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
    }
}

internal sealed class FixtureExtractionProvider(bool available = true) : ITaskExtractionProvider
{
    public bool IsAvailable => available;
    public string ProviderName => available ? "fake" : "disabled";
    public string? UnavailableReasonCode => available ? null : "ai_not_configured";
    public int ReservationTokens { get; set; } = 1000;
    public int Calls { get; private set; }
    public TaskExtractionProviderInput? LastInput { get; private set; }
    public Exception? Failure { get; set; }
    public Func<TaskExtractionProviderInput, Task>? BeforeReturn { get; set; }
    public int CalculateReservationTokens(TaskExtractionProviderInput input) => ReservationTokens;

    public async Task<TaskExtractionProviderResult> ExtractAsync(TaskExtractionProviderInput input, CancellationToken ct = default)
    {
        Calls++;
        LastInput = input;
        if (Failure is not null) throw Failure;
        if (BeforeReturn is not null) await BeforeReturn(input);
        var first = "Олена завтра перевірить API оплати.";
        var second = "Сергій до 2026-10-16 підготує міграцію.";
        object Candidate(string title, int id, string assignee, string due, string quote) => new
        {
            title, description = "", proposedAssigneeId = id, originalAssigneeText = assignee,
            proposedDueAtUtc = (string?)null, originalDueText = due,
            sourceQuote = quote, sourceStart = input.NotesText.IndexOf(quote, StringComparison.Ordinal), sourceLength = quote.Length,
            isSuggestion = false, interpretations = Array.Empty<string>(), clarificationReasons = Array.Empty<string>()
        };
        var json = JsonSerializer.Serialize(new { items = new[]
        {
            Candidate("Перевірити API оплати", 101, "Олена", "завтра", first),
            Candidate("Підготувати міграцію", 102, "Сергій", "2026-10-16", second)
        } });
        return new TaskExtractionProviderResult(json, Model: "synthetic-fixture");
    }
}

// This injects a late relational failure on an existing outbox insert. It proves
// atomic rollback; it does not emulate concurrent SQL Server lock behavior.
internal sealed class AiOutboxFailureInterceptor : DbCommandInterceptor
{
    public bool Enabled { get; set; }
    public bool Triggered { get; private set; }
    private void FailIfEnabled(DbCommand command)
    {
        if (!Enabled || !command.CommandText.Contains("INSERT INTO \"NotificationOutbox\"", StringComparison.Ordinal)) return;
        Triggered = true;
        throw new SqliteException("Synthetic outbox constraint failure", 19);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        FailIfEnabled(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        FailIfEnabled(command);
        return ValueTask.FromResult(result);
    }
}

internal sealed class MutableAiClock(DateTimeOffset? initial = null) : TimeProvider
{
    private DateTimeOffset _now = initial ?? new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan amount) => _now = _now.Add(amount);
}

internal sealed class CountingQueue : IQueueService
{
    public int Calls { get; private set; }
    public Task<bool> PostValue(string message, string? queueName = null, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(true);
    }
}
