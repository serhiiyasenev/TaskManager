using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BLL.Interfaces;
using DAL.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Integration;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.AI;

public sealed class SqlServerAiDraftConcurrencyTests
{
    [SqlServerFact]
    public async Task SameKeyConcurrentCommits_ReplayOneDurableReceiptAndOneMaterialization()
    {
        await using var database = await OwnedAiDatabase.CreateAsync();
        var connections = new SqlConnectionAudit();
        var fixture = await CreateFixtureAsync(database, connections);
        using var factory = fixture.Factory;
        using var first = factory.CreateUserClient(fixture.OwnerId);
        using var second = factory.CreateUserClient(fixture.OwnerId);
        var draft = await ExtractAsync(first, fixture.ProjectId);
        var selection = new[] { AiDraftContractTests.Item(draft, 0, assigneeId: fixture.OwnerId) };
        connections.Reset();
        var responses = await TogetherAsync(
            () => AiDraftContractTests.CommitAsync(first, draft, selection, "sql-same-key"),
            () => AiDraftContractTests.CommitAsync(second, draft, selection, "sql-same-key"));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(await responses[0].Content.ReadAsStringAsync(), await responses[1].Content.ReadAsStringAsync());
        Assert.True(connections.Count >= 2, "Concurrent requests must use independent SQL Server connections.");
        await AssertOneCommittedItemAsync(factory);
        // Replaying after the task itself disappears never recreates it.
        var receipt = await responses[0].Content.ReadFromJsonAsync<JsonElement>();
        var taskId = receipt.GetProperty("items")[0].GetProperty("taskId").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await first.DeleteAsync($"/api/tasks/{taskId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AiDraftContractTests.CommitAsync(second, draft, selection, "sql-same-key")).StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Empty(await db.Tasks.ToListAsync());
            var marker = await db.Set<AiTaskMaterialization>().SingleAsync();
            Assert.Equal(taskId, marker.OriginalTaskId);
            Assert.Null(marker.LiveTaskId);
        });
    }

    [SqlServerFact]
    public async Task DifferentKeysConcurrentCommits_SameDraftItemIsMaterializedOnce()
    {
        await using var database = await OwnedAiDatabase.CreateAsync();
        var connections = new SqlConnectionAudit();
        var fixture = await CreateFixtureAsync(database, connections);
        using var factory = fixture.Factory;
        using var first = factory.CreateUserClient(fixture.OwnerId);
        using var second = factory.CreateUserClient(fixture.OwnerId);
        var draft = await ExtractAsync(first, fixture.ProjectId);
        var selection = new[] { AiDraftContractTests.Item(draft, 0, assigneeId: fixture.PeerId) };
        connections.Reset();
        var responses = await TogetherAsync(
            () => AiDraftContractTests.CommitAsync(first, draft, selection, "sql-key-a"),
            () => AiDraftContractTests.CommitAsync(second, draft, selection, "sql-key-b"));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, responses.Select(value => value.StatusCode).Order().ToArray());
        Assert.True(connections.Count >= 2, "Concurrent requests must use independent SQL Server connections.");
        await AssertOneCommittedItemAsync(factory);
        var current = await first.GetFromJsonAsync<JsonElement>($"/api/ai/task-extractions/{draft.GetProperty("id").GetGuid()}");
        Assert.NotEqual(draft.GetProperty("version").GetGuid(), current.GetProperty("version").GetGuid());
        await AiDraftContractTests.AssertProblemAsync(await AiDraftContractTests.CommitAsync(first, current,
            [AiDraftContractTests.Item(current, 0, assigneeId: fixture.PeerId)], "sql-key-after-winner"), HttpStatusCode.Conflict);
        await AssertOneCommittedItemAsync(factory);
    }

    [SqlServerFact]
    public async Task ProductQuotaAcrossConcurrentUsers_AdmitsOneInferenceAndNeverOverspends()
    {
        await using var database = await OwnedAiDatabase.CreateAsync();
        var connections = new SqlConnectionAudit();
        var provider = new FixtureExtractionProvider { ReservationTokens = 500, BeforeReturn = _ => Task.Delay(75) };
        var fixture = await CreateFixtureAsync(database, connections, provider, new Dictionary<string, string?>
        {
            ["AI:UserDailyTokenBudget"] = "1000", ["AI:ProductDailyTokenBudget"] = "750"
        });
        using var factory = fixture.Factory;
        using var first = factory.CreateUserClient(fixture.OwnerId);
        using var second = factory.CreateUserClient(fixture.PeerId);
        connections.Reset();
        var responses = await TogetherAsync(
            () => first.PostAsJsonAsync("/api/ai/task-extractions", Extraction(fixture.ProjectId)),
            () => second.PostAsJsonAsync("/api/ai/task-extractions", Extraction(fixture.ProjectId)));
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.TooManyRequests }, responses.Select(value => value.StatusCode).Order().ToArray());
        Assert.Equal(1, provider.Calls);
        Assert.True(connections.Count >= 2, "Concurrent requests must use independent SQL Server connections.");
        await factory.WithDatabaseAsync(async db =>
        {
            var product = await db.Set<AiDailyUsage>().SingleAsync(value => value.ScopeKey == "product");
            Assert.Equal(500, product.ReservedTokens);
            Assert.Equal(1, await db.Set<AiDailyUsage>().CountAsync(value => value.ScopeKey.StartsWith("user:")));
            Assert.Single(await db.Set<AiTaskDraft>().ToListAsync());
            Assert.Empty(await db.Tasks.ToListAsync());
            Assert.Empty(await db.NotificationOutbox.ToListAsync());
        });
    }

    private static async Task AssertOneCommittedItemAsync(DataIsolationFactory factory) => await factory.WithDatabaseAsync(async db =>
    {
        var task = await db.Tasks.SingleAsync();
        var receipt = await db.Set<AiTaskCommitReceipt>().SingleAsync();
        var materialization = await db.Set<AiTaskMaterialization>().SingleAsync();
        var outbox = await db.NotificationOutbox.SingleAsync();
        Assert.Equal(task.Id, materialization.OriginalTaskId);
        Assert.Equal(task.Id, materialization.LiveTaskId);
        Assert.Equal(outbox.Id, materialization.OutboxMessageId);
        Assert.Equal(task.Id, outbox.TaskId);
        Assert.NotEqual(Guid.Empty, task.Version);
        Assert.Equal(NotificationOutboxKind.TaskCreated, outbox.Kind);
        Assert.Equal(NotificationOutboxStatus.Pending, outbox.Status);
        Assert.Null(outbox.PublishedAtUtc);
        var stored = JsonSerializer.Deserialize<BLL.Models.AI.AiTaskCommitResponse>(receipt.ResponseJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(task.Version, stored.Items[0].TaskVersion);
        Assert.Equal((await db.Set<AiTaskDraft>().SingleAsync()).Version, stored.DraftVersion);
    });

    private static object Extraction(int projectId) => new
    {
        notesText = AiDraftContractTests.Notes, projectId, noteDate = "2026-10-12", timeZoneId = "Europe/Kyiv", locale = "uk"
    };
    private static async Task<JsonElement> ExtractAsync(HttpClient client, int projectId)
    {
        var response = await client.PostAsJsonAsync("/api/ai/task-extractions", Extraction(projectId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage[]> TogetherAsync(Func<Task<HttpResponseMessage>> first,
        Func<Task<HttpResponseMessage>> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> RunAsync(Func<Task<HttpResponseMessage>> operation)
        {
            await start.Task;
            return await operation();
        }
        var a = RunAsync(first); var b = RunAsync(second);
        start.SetResult();
        return await Task.WhenAll(a, b);
    }

    private static async Task<SqlFixture> CreateFixtureAsync(OwnedAiDatabase database, SqlConnectionAudit connections,
        FixtureExtractionProvider? provider = null, IReadOnlyDictionary<string, string?>? configuration = null)
    {
        provider ??= new FixtureExtractionProvider();
        var factory = await DataIsolationFactory.CreateAsync(sqlConnection: database.ConnectionString, interceptor: connections,
            additionalConfiguration: configuration, configureServices: services =>
            {
                services.RemoveAll<ITaskExtractionProvider>();
                services.AddSingleton<ITaskExtractionProvider>(provider);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new MutableAiClock());
            });
        try
        {
            var ownerId = 0; var peerId = 0; var projectId = 0;
            await factory.WithDatabaseAsync(async db =>
            {
                var owner = Account("owner.ai@example.test", "Олена", "Коваль");
                var peer = Account("peer.ai@example.test", "Сергій", "Мельник");
                db.Users.AddRange(owner, peer); await db.SaveChangesAsync();
                var team = new Team { Name = "AI SQL team", OwnerId = owner.Id };
                db.Teams.Add(team); await db.SaveChangesAsync();
                owner.TeamId = team.Id; peer.TeamId = team.Id;
                var project = new Project { Name = "AI SQL project", Description = "Scoped AI concurrency", TeamId = team.Id,
                    AuthorId = owner.Id, Deadline = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) };
                db.Projects.Add(project); await db.SaveChangesAsync();
                ownerId = owner.Id; peerId = peer.Id; projectId = project.Id;
            });
            return new(factory, ownerId, peerId, projectId);
        }
        catch { factory.Dispose(); throw; }
    }

    private static User Account(string email, string firstName, string lastName) => new(email, firstName, lastName, email)
    {
        EmailConfirmed = true, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(),
        RegisteredAt = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), BirthDay = new DateTime(1990, 1, 1),
        SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
    };
    private sealed record SqlFixture(DataIsolationFactory Factory, int OwnerId, int PeerId, int ProjectId);

    private sealed class SqlConnectionAudit : DbConnectionInterceptor
    {
        private readonly ConcurrentDictionary<Guid, byte> _connections = new();
        public int Count => _connections.Count;
        public void Reset() => _connections.Clear();
        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (connection is SqlConnection sql) _connections.TryAdd(sql.ClientConnectionId, 0);
            return Task.CompletedTask;
        }
    }

    private sealed class OwnedAiDatabase(string serverConnection, string name) : IAsyncDisposable
    {
        private const string Prefix = "taskmanager_ai_test_";
        public string ConnectionString { get; } = new SqlConnectionStringBuilder(serverConnection) { InitialCatalog = name }.ConnectionString;
        public static async Task<OwnedAiDatabase> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("TASKMANAGER_TEST_SQL_SERVER");
            if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("The isolated SQL test server is not configured.");
            var server = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
            var name = Prefix + Guid.NewGuid().ToString("N");
            Validate(name);
            await using var connection = new SqlConnection(server.ConnectionString);
            await connection.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{name}]", connection);
            await create.ExecuteNonQueryAsync();
            return new(server.ConnectionString, name);
        }
        public async ValueTask DisposeAsync()
        {
            Validate(name);
            using (var owned = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(owned);
            await using var connection = new SqlConnection(serverConnection);
            await connection.OpenAsync();
            await using var drop = new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", connection);
            await drop.ExecuteNonQueryAsync();
        }
        private static void Validate(string candidate)
        {
            if (!candidate.StartsWith(Prefix, StringComparison.Ordinal) || candidate.Length != Prefix.Length + 32
                || !Guid.TryParseExact(candidate[Prefix.Length..], "N", out _))
                throw new InvalidOperationException("Refusing operations outside the disposable AI test database namespace.");
        }
    }
}
