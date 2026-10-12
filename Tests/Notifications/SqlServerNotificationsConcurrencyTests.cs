using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BLL.Interfaces;
using BLL.Models.Messaging;
using BLL.Services.Notifications;
using DAL.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.AI;
using Tests.Integration;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Notifications;

public sealed class SqlServerNotificationsConcurrencyTests
{
    [SqlServerFact]
    public async Task ConcurrentDuplicateDeliveries_CommitOneNotificationAndOneMarkerOnIndependentConnections()
    {
        await using var database = await NotificationTestDatabase.CreateAsync();
        var connections = new ConnectionAudit();
        var fixture = await CreateFixtureAsync(database, connections);
        using var factory = fixture.Factory;
        connections.Reset();
        var results = await TogetherAsync(
            () => ProcessAsync(fixture),
            () => ProcessAsync(fixture));
        Assert.Equal(1, results.Count(value => value.Created));
        Assert.All(results, value => Assert.False(value.Suppressed));
        Assert.True(connections.Count >= 2, "Duplicate deliveries must use independent SQL Server connections.");
        await factory.WithDatabaseAsync(async db =>
        {
            var notification = Assert.Single(await db.UserNotifications.ToListAsync());
            Assert.Equal(fixture.MessageId, notification.Id);
            Assert.Equal(fixture.OwnerId, notification.OwnerId);
            Assert.Equal(3, notification.Kind);
            Assert.Equal("SQL inbox task", notification.TaskName);
            Assert.Equal("SQL inbox project", notification.ProjectName);
            var marker = Assert.Single(await db.NotificationProcessingMarkers.ToListAsync());
            Assert.Equal(NotificationProcessingDisposition.Delivered, marker.Disposition);
            Assert.Equal(NotificationOutboxStatus.Pending, (await db.NotificationOutbox.SingleAsync()).Status);
        });
        // ACK-loss replay remains successful but cannot produce a second hint.
        Assert.False((await ProcessAsync(fixture)).Created);
        using var client = factory.CreateUserClient(fixture.OwnerId);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(TimeSpan.Zero, page.GetProperty("items")[0].GetProperty("occurredAtUtc").GetDateTimeOffset().Offset);
    }

    [SqlServerFact]
    public async Task ConcurrentMarkRead_PreservesFirstTimestampAndFreshReassignmentHidesHistory()
    {
        await using var database = await NotificationTestDatabase.CreateAsync();
        var connections = new ConnectionAudit();
        var fixture = await CreateFixtureAsync(database, connections);
        using var factory = fixture.Factory;
        await ProcessAsync(fixture);
        using var first = factory.CreateUserClient(fixture.OwnerId);
        using var second = factory.CreateUserClient(fixture.OwnerId);
        connections.Reset();
        var results = await TogetherAsync(
            () => first.PutAsync($"/api/notifications/{fixture.MessageId}/read", null),
            () => second.PutAsync($"/api/notifications/{fixture.MessageId}/read", null));
        Assert.All(results, value => Assert.Equal(HttpStatusCode.OK, value.StatusCode));
        var a = await results[0].Content.ReadFromJsonAsync<JsonElement>();
        var b = await results[1].Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("readAtUtc").GetDateTimeOffset(), b.GetProperty("readAtUtc").GetDateTimeOffset());
        Assert.Equal(fixture.Clock.GetUtcNow(), a.GetProperty("readAtUtc").GetDateTimeOffset());
        Assert.True(connections.Count >= 2, "Concurrent read updates must use independent SQL Server connections.");
        fixture.Clock.Advance(TimeSpan.FromHours(2));
        var again = await first.PutAsync($"/api/notifications/{fixture.MessageId}/read", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(a.GetProperty("readAtUtc").GetDateTimeOffset(), (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("readAtUtc").GetDateTimeOffset());
        var page = await first.GetFromJsonAsync<JsonElement>("/api/notifications?unreadOnly=true");
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, page.GetProperty("unreadCount").GetInt32());
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        await factory.WithDatabaseAsync(async db =>
        {
            (await db.Tasks.SingleAsync(value => value.Id == fixture.TaskId)).PerformerId = fixture.PeerId;
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await first.PutAsync($"/api/notifications/{fixture.MessageId}/read", null)).StatusCode);
        var hidden = await first.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Equal(0, hidden.GetProperty("totalCount").GetInt32());
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Single(await db.UserNotifications.ToListAsync());
            Assert.Single(await db.NotificationProcessingMarkers.ToListAsync());
        });
    }

    private static async Task<NotificationProcessingResult> ProcessAsync(Fixture fixture)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationMessageProcessor>()
            .ProcessAsync(fixture.MessageId, fixture.Payload);
    }

    private static async Task<T[]> TogetherAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<T> RunAsync(Func<Task<T>> operation) { await start.Task; return await operation(); }
        var a = RunAsync(first); var b = RunAsync(second);
        start.SetResult();
        return await Task.WhenAll(a, b);
    }

    internal static async Task<Fixture> CreateFixtureAsync(NotificationTestDatabase database, IInterceptor? interceptor = null)
    {
        var clock = new MutableAiClock();
        var factory = await DataIsolationFactory.CreateAsync(sqlConnection: database.ConnectionString, interceptor: interceptor,
            configureServices: services =>
            {
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
                services.RemoveAll<INotificationInboxService>(); services.AddScoped<INotificationInboxService, NotificationInboxService>();
                services.RemoveAll<INotificationMessageProcessor>(); services.AddScoped<INotificationMessageProcessor, NotificationMessageProcessor>();
            });
        try
        {
            var ownerId = 0; var peerId = 0; var taskId = 0; var projectId = 0;
            var id = Guid.NewGuid(); var payload = "";
            await factory.WithDatabaseAsync(async db =>
            {
                var owner = Account("owner.inbox@example.test"); var peer = Account("peer.inbox@example.test");
                db.Users.AddRange(owner, peer); await db.SaveChangesAsync();
                var team = new Team { Name = "SQL inbox team", OwnerId = owner.Id };
                db.Teams.Add(team); await db.SaveChangesAsync();
                owner.TeamId = team.Id; peer.TeamId = team.Id;
                var project = new Project { Name = "SQL inbox project", Description = "Inbox concurrency", AuthorId = owner.Id, TeamId = team.Id };
                db.Projects.Add(project); await db.SaveChangesAsync();
                var task = new DAL.Entities.Task { Name = "SQL inbox task", Description = "Inbox concurrency", PerformerId = owner.Id,
                    ProjectId = project.Id, CreatedAt = clock.GetUtcNow().UtcDateTime };
                db.Tasks.Add(task); await db.SaveChangesAsync();
                payload = JsonSerializer.Serialize(new TaskNotificationEnvelope { Type = NotificationEventType.TaskCreated,
                    CorrelationId = id.ToString("N"), TaskCreated = new(id, task.Id, task.Name, owner.Id, project.Id, project.Name, task.CreatedAt) });
                db.NotificationOutbox.Add(new NotificationOutboxMessage { Id = id, TaskId = task.Id, Kind = NotificationOutboxKind.TaskCreated,
                    QueueName = "sql-inbox-test", Payload = payload, CreatedAtUtc = clock.GetUtcNow().UtcDateTime });
                await db.SaveChangesAsync(); ownerId = owner.Id; peerId = peer.Id; taskId = task.Id; projectId = project.Id;
            });
            return new(factory, ownerId, peerId, taskId, projectId, id, payload, clock);
        }
        catch { factory.Dispose(); throw; }
    }

    private static User Account(string email) => new(email, "Inbox", "Member", email) { EmailConfirmed = true,
        NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(), BirthDay = new DateTime(1990, 1, 1),
        SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
    internal sealed record Fixture(DataIsolationFactory Factory, int OwnerId, int PeerId, int TaskId, int ProjectId,
        Guid MessageId, string Payload, MutableAiClock Clock);

    private sealed class ConnectionAudit : DbConnectionInterceptor
    {
        private readonly ConcurrentDictionary<Guid, byte> _connections = new();
        public int Count => _connections.Count;
        public void Reset() => _connections.Clear();
        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (connection is SqlConnection sql) _connections.TryAdd(sql.ClientConnectionId, 0);
            return Task.CompletedTask;
        }
    }

}
