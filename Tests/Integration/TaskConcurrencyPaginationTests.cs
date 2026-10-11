using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DAL.Enum;
using Microsoft.EntityFrameworkCore;
using TaskEntity = DAL.Entities.Task;
using Xunit;

namespace Tests.Integration;

public sealed class TaskConcurrencyPaginationTests
{
    [Fact]
    public async Task TaskReadAndCreate_ReturnNonEmptyServerVersions()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var member = factory.CreateUserClient(101);
        var detail = await ReadTaskAsync(member, 101);
        Assert.NotEqual(Guid.Empty, Version(detail));
        var list = await member.GetFromJsonAsync<JsonElement>("/api/tasks");
        Assert.All(list.EnumerateArray(), task => Assert.NotEqual(Guid.Empty, Version(task)));
        var create = await member.PostAsJsonAsync("/api/tasks", Write("New versioned task", expectedVersion: Guid.NewGuid()));
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, Version(created));
        Assert.Equal(Version(created), Version(await ReadTaskAsync(member, created.GetProperty("id").GetInt32())));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("reminder")]
    [InlineData("delete")]
    public async Task BrowserMutation_WithoutExpectedVersion_Returns428AndPreservesTask(string mutation)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var browser = await BrowserAsync(factory);
        var before = await SnapshotAsync(factory);
        var result = await MutateAsync(browser, mutation, null);
        await AssertProblemAsync(result, (HttpStatusCode)428);
        Assert.Equal(before, await SnapshotAsync(factory));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("reminder")]
    [InlineData("delete")]
    public async Task StaleBrowserMutation_Returns409AndPreservesOccurrenceAndOutbox(string mutation)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        await factory.WithDatabaseAsync(async db =>
        {
            var task = await db.Tasks.SingleAsync(value => value.Id == 101);
            task.DueDate = DateTime.UtcNow.AddDays(1);
            task.ReminderEnabled = true;
            task.ReminderOffsetMinutes = 15;
            task.ReminderSentAt = DateTime.UtcNow.AddMinutes(-2);
            task.ReminderNotificationId = Guid.NewGuid();
            task.EscalationEnabled = true;
            task.EscalationDelayMinutes = 30;
            task.EscalationNotificationId = Guid.NewGuid();
            await db.SaveChangesAsync();
        });
        using var browser = await BrowserAsync(factory);
        var before = await SnapshotAsync(factory);
        var result = await MutateAsync(browser, mutation, Guid.NewGuid());
        await AssertProblemAsync(result, HttpStatusCode.Conflict);
        Assert.Equal(before, await SnapshotAsync(factory));
    }

    [Fact]
    public async Task TwoBrowserEdits_OnlyFirstExpectedVersionCommits()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var first = await BrowserAsync(factory);
        using var second = await BrowserAsync(factory);
        var original = Version(await ReadTaskAsync(first, 101));
        var saved = await first.PutAsJsonAsync("/api/tasks/101", Write("First browser edit", original));
        saved.EnsureSuccessStatusCode();
        var current = Version(await saved.Content.ReadFromJsonAsync<JsonElement>());
        Assert.NotEqual(Guid.Empty, current);
        Assert.NotEqual(original, current);
        await AssertProblemAsync(await second.PutAsJsonAsync("/api/tasks/101", Write("Stale browser edit", original)), HttpStatusCode.Conflict);
        Assert.Equal("First browser edit", (await ReadTaskAsync(second, 101)).GetProperty("name").GetString());
        Assert.Equal(current, Version(await ReadTaskAsync(second, 101)));
    }

    [Fact]
    public async Task IfMatch_AppliesToReminderAndDeleteAndVersionRotates()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var browser = await BrowserAsync(factory);
        var original = Version(await ReadTaskAsync(browser, 101));
        browser.DefaultRequestHeaders.Add("If-Match", $"\"{original}\"");
        var saved = await browser.PutAsJsonAsync("/api/tasks/101/reminder", Reminder(null));
        saved.EnsureSuccessStatusCode();
        var current = Version(await saved.Content.ReadFromJsonAsync<JsonElement>());
        Assert.NotEqual(original, current);
        await AssertProblemAsync(await browser.DeleteAsync("/api/tasks/101"), HttpStatusCode.Conflict);
        browser.DefaultRequestHeaders.Remove("If-Match");
        browser.DefaultRequestHeaders.Add("If-Match", $"\"{current}\"");
        Assert.Equal(HttpStatusCode.OK, (await browser.DeleteAsync("/api/tasks/101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/api/tasks/101")).StatusCode);
    }

    [Theory]
    [InlineData("weak")]
    [InlineData("unquoted")]
    [InlineData("wildcard")]
    [InlineData("malformed")]
    [InlineData("multiple")]
    public async Task IfMatch_RequiresOneStrongGuidAndDoesNotWrite(string kind)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var browser = await BrowserAsync(factory);
        var before = await SnapshotAsync(factory);
        var guid = Guid.NewGuid();
        var header = kind switch
        {
            "weak" => $"W/\"{guid}\"",
            "unquoted" => guid.ToString(),
            "wildcard" => "*",
            "multiple" => $"\"{guid}\", \"{Guid.NewGuid()}\"",
            _ => "\"invalid-version\""
        };
        browser.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", header);
        await AssertProblemAsync(await browser.DeleteAsync("/api/tasks/101"), HttpStatusCode.BadRequest);
        Assert.Equal(before, await SnapshotAsync(factory));
    }

    [Fact]
    public async Task ContradictingBodyAndIfMatchVersions_AreRejectedWithoutWrite()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var browser = await BrowserAsync(factory);
        var before = await SnapshotAsync(factory);
        browser.DefaultRequestHeaders.Add("If-Match", $"\"{Guid.NewGuid()}\"");
        await AssertProblemAsync(await browser.PutAsJsonAsync("/api/tasks/101", Write("Ambiguous edit", Guid.NewGuid())), HttpStatusCode.BadRequest);
        Assert.Equal(before, await SnapshotAsync(factory));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("reminder")]
    [InlineData("delete")]
    public async Task ForeignVersionedMutation_RemainsNotFound(string mutation)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var browser = await BrowserAsync(factory);
        var version = Guid.NewGuid();
        browser.DefaultRequestHeaders.Add("If-Match", $"\"{version}\"");
        var response = mutation switch
        {
            "update" => await browser.PutAsJsonAsync("/api/tasks/102", Write("Foreign mutation", version)),
            "reminder" => await browser.PutAsJsonAsync("/api/tasks/102/reminder", Reminder(version)),
            _ => await browser.DeleteAsync("/api/tasks/102")
        };
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
        await factory.WithDatabaseAsync(async db => Assert.Equal("Team B private task", (await db.Tasks.SingleAsync(value => value.Id == 102)).Name));
    }

    [Fact]
    public async Task ExplicitBearer_LegacyWriteWithoutVersionStillWorks()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var native = factory.CreateUserClient(101);
        Assert.Equal(HttpStatusCode.OK, (await native.PutAsJsonAsync("/api/tasks/101", Write("Native compatible update"))).StatusCode);
    }

    [Fact]
    public async Task TrackedConcurrentNameEdits_SecondSaveConflicts()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        await factory.WithDatabaseAsync(async first =>
        {
            var firstTask = await first.Tasks.SingleAsync(value => value.Id == 101);
            await factory.WithDatabaseAsync(async second =>
            {
                var secondTask = await second.Tasks.SingleAsync(value => value.Id == 101);
                firstTask.Name = "Committed first";
                await first.SaveChangesAsync();
                secondTask.Name = "Lost concurrent name edit";
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            });
        });
        await factory.WithDatabaseAsync(async db => Assert.Equal("Committed first", (await db.Tasks.SingleAsync(value => value.Id == 101)).Name));
    }

    [Fact]
    public async Task SchedulerOccurrenceMutation_InvalidatesBrowserVersion()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var browser = await BrowserAsync(factory);
        var before = Version(await ReadTaskAsync(browser, 101));
        await factory.WithDatabaseAsync(async db =>
        {
            var task = await db.Tasks.SingleAsync(value => value.Id == 101);
            task.ReminderSentAt = DateTime.UtcNow;
            task.ReminderNotificationId = Guid.NewGuid();
            await db.SaveChangesAsync();
        });
        await AssertProblemAsync(await browser.PutAsJsonAsync("/api/tasks/101", Write("Stale after scheduling", before)), HttpStatusCode.Conflict);
        Assert.NotEqual(before, Version(await ReadTaskAsync(browser, 101)));
    }

    [Fact]
    public async Task Paging_UsesScopedTotalsStableTiesAndRequestedPage()
    {
        using var factory = await PageFixtureAsync();
        using var member = factory.CreateUserClient(101);
        var first = await PageAsync(member, "page=1&pageSize=2&sort=name&direction=asc");
        Assert.Equal(4, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, first.GetProperty("page").GetInt32());
        Assert.Equal(2, first.GetProperty("pageSize").GetInt32());
        Assert.Equal(new[] { 101, 103 }, Ids(first));
        Assert.All(first.GetProperty("items").EnumerateArray(), task => Assert.NotEqual(Guid.Empty, Version(task)));
        var second = await PageAsync(member, "page=2&pageSize=2&sort=name&direction=asc");
        Assert.Equal(4, second.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { 106, 105 }, Ids(second));
        var empty = await PageAsync(member, "page=3&pageSize=2");
        Assert.Equal(4, empty.GetProperty("totalCount").GetInt32());
        Assert.Empty(Ids(empty));
    }

    [Theory]
    [InlineData("projectId=101", 101, 103, 105)]
    [InlineData("projectId=103", 106)]
    [InlineData("state=InProgress&performerId=102&search=needle", 103)]
    [InlineData("search=Needle", 101, 103)]
    [InlineData("overdue=true", 103)]
    [InlineData("overdue=false", 101, 105, 106)]
    public async Task Paging_FiltersComposeBeforeCounting(string query, params int[] expected)
    {
        using var factory = await PageFixtureAsync();
        using var member = factory.CreateUserClient(101);
        var page = await PageAsync(member, query + "&sort=created&direction=asc");
        Assert.Equal(expected.Length, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(expected, Ids(page));
    }

    [Theory]
    [InlineData("projectId=102")]
    [InlineData("performerId=103")]
    [InlineData("search=Secret")]
    public async Task Paging_ForeignFiltersNeverExposeRowsOrCounts(string query)
    {
        using var factory = await PageFixtureAsync();
        using var member = factory.CreateUserClient(101);
        var page = await PageAsync(member, query);
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        Assert.Empty(Ids(page));
    }

    [Fact]
    public async Task Paging_DueWindowAndDueSortUseUtcBoundaries()
    {
        using var factory = await PageFixtureAsync();
        using var member = factory.CreateUserClient(101);
        DateTime due = default;
        await factory.WithDatabaseAsync(async db => due = DateTime.SpecifyKind((await db.Tasks.SingleAsync(value => value.Id == 101)).DueDate!.Value, DateTimeKind.Utc));
        var page = await PageAsync(member, $"dueFrom={Uri.EscapeDataString(due.ToString("O", CultureInfo.InvariantCulture))}&dueTo={Uri.EscapeDataString(due.ToString("O", CultureInfo.InvariantCulture))}&sort=due&direction=desc");
        Assert.Equal(new[] { 101 }, Ids(page));
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { 105, 103, 101, 106 }, Ids(await PageAsync(member, "sort=due&direction=asc")));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("sort=unsafe")]
    [InlineData("direction=sideways")]
    [InlineData("state=99")]
    [InlineData("dueFrom=2030-01-02T00%3A00%3A00Z&dueTo=2030-01-01T00%3A00%3A00Z")]
    public async Task Paging_InvalidInputIsProblem400(string query)
    {
        using var factory = await PageFixtureAsync();
        using var member = factory.CreateUserClient(101);
        await AssertProblemAsync(await member.GetAsync("/api/tasks/page?" + query), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Paging_RequiresAuthenticationAndFreshCurrentMembership()
    {
        using var factory = await PageFixtureAsync();
        using var anonymous = factory.CreateUserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/tasks/page")).StatusCode);
        using var member = factory.CreateUserClient(101);
        await factory.WithDatabaseAsync(async db =>
        {
            (await db.Users.SingleAsync(user => user.Id == 101)).TeamId = null;
            await db.SaveChangesAsync();
        });
        var page = await PageAsync(member, "");
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        Assert.Empty(Ids(page));
    }

    private static async Task<HttpClient> BrowserAsync(DataIsolationFactory factory)
    {
        var client = factory.CreateUserClient(101);
        client.DefaultRequestHeaders.Authorization = null;
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        return client;
    }

    private static object Write(string name, Guid? expectedVersion = null) => new
    {
        projectId = 101, performerId = 101, name, description = "Versioned edit", state = 0,
        reminderEnabled = false, escalationEnabled = false, expectedVersion
    };
    private static object Reminder(Guid? expectedVersion) => new
    {
        dueDate = DateTime.UtcNow.AddDays(2), reminderEnabled = true, reminderOffsetMinutes = 15,
        escalationEnabled = true, escalationDelayMinutes = 30, expectedVersion
    };

    private static async Task<HttpResponseMessage> MutateAsync(HttpClient client, string mutation, Guid? expectedVersion)
    {
        if (mutation == "delete" && expectedVersion.HasValue)
            client.DefaultRequestHeaders.Add("If-Match", $"\"{expectedVersion}\"");
        return mutation switch
        {
            "update" => await client.PutAsJsonAsync("/api/tasks/101", Write("Changed task", expectedVersion)),
            "reminder" => await client.PutAsJsonAsync("/api/tasks/101/reminder", Reminder(expectedVersion)),
            _ => await client.DeleteAsync("/api/tasks/101")
        };
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expected, body.GetProperty("status").GetInt32());
    }
    private static async Task<JsonElement> ReadTaskAsync(HttpClient client, int id) => await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}");
    private static Guid Version(JsonElement task) => task.TryGetProperty("version", out var version) && version.TryGetGuid(out var value) ? value : Guid.Empty;
    private static async Task<JsonElement> PageAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/tasks/page?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static int[] Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt32()).ToArray();

    private static async Task<string> SnapshotAsync(DataIsolationFactory factory)
    {
        string snapshot = "";
        await factory.WithDatabaseAsync(async db =>
        {
            var task = await db.Tasks.AsNoTracking().SingleAsync(value => value.Id == 101);
            snapshot = JsonSerializer.Serialize(new
            {
                task.Name, task.Description, task.State, task.DueDate, task.ReminderEnabled, task.ReminderOffsetMinutes,
                task.ReminderSentAt, task.ReminderNotificationId, task.EscalationEnabled, task.EscalationDelayMinutes,
                task.EscalationSentAt, task.EscalationNotificationId, outbox = await db.NotificationOutbox.CountAsync()
            });
        });
        return snapshot;
    }

    private static async Task<DataIsolationFactory> PageFixtureAsync()
    {
        var factory = await DataIsolationFactory.CreateAsync();
        var now = DateTime.UtcNow;
        await factory.WithDatabaseAsync(async db =>
        {
            var alpha = await db.Tasks.SingleAsync(task => task.Id == 101);
            alpha.Name = "Alpha"; alpha.Description = "Needle only mine"; alpha.DueDate = now.AddDays(1); alpha.CreatedAt = now;
            var beta = await db.Tasks.SingleAsync(task => task.Id == 103);
            beta.Name = "Beta"; beta.Description = "Needle peer"; beta.State = TaskState.InProgress; beta.DueDate = now.AddDays(-1); beta.CreatedAt = now.AddMinutes(1);
            var foreign = await db.Tasks.SingleAsync(task => task.Id == 102);
            foreign.Name = "Secret Needle"; foreign.DueDate = now.AddDays(-1);
            db.Tasks.AddRange(
                new TaskEntity { Id = 105, ProjectId = 101, PerformerId = 101, Name = "Gamma", Description = "Resolved", State = TaskState.Done, DueDate = now.AddDays(-2), CreatedAt = now.AddMinutes(2) },
                new TaskEntity { Id = 106, ProjectId = 103, PerformerId = 102, Name = "Beta", Description = "Next", DueDate = now.AddDays(2), CreatedAt = now.AddMinutes(3) });
            await db.SaveChangesAsync();
        });
        return factory;
    }
}
