using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DAL.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Integration;

public sealed class SqlServerTaskLifecycleTests
{
    [SqlServerFact]
    public async Task ScopedPagingAndVersionedWrites_UseRealSqlOrderingEscapesAndConcurrency()
    {
        await using var database = await OwnedTaskDatabase.CreateAsync();
        using var factory = await DataIsolationFactory.CreateAsync(sqlConnection: database.ConnectionString);
        var ownerId = 0;
        var peerId = 0;
        var outsiderId = 0;
        await factory.WithDatabaseAsync(async db =>
        {
            var owner = Account("sql.task.owner@example.test");
            var peer = Account("sql.task.peer@example.test");
            var outsider = Account("sql.task.outsider@example.test");
            db.Users.AddRange(owner, peer, outsider);
            await db.SaveChangesAsync();
            ownerId = owner.Id; peerId = peer.Id; outsiderId = outsider.Id;
        });
        using var ownerClient = factory.CreateUserClient(ownerId);
        using var outsiderClient = factory.CreateUserClient(outsiderId);
        var teamId = await CreateAsync(ownerClient, "/api/teams", new { name = "SQL task team" });
        var foreignTeamId = await CreateAsync(outsiderClient, "/api/teams", new { name = "SQL foreign task team" });
        await factory.WithDatabaseAsync(async db =>
        {
            (await db.Users.SingleAsync(user => user.Id == peerId)).TeamId = teamId;
            await db.SaveChangesAsync();
        });
        var projectId = await CreateAsync(ownerClient, "/api/projects", new { name = "SQL task project", description = "Scoped", teamId });
        var foreignProjectId = await CreateAsync(outsiderClient, "/api/projects", new { name = "SQL secret project", description = "Foreign", teamId = foreignTeamId });
        var due = DateTime.UtcNow.AddDays(1);
        var firstId = await CreateAsync(ownerClient, "/api/tasks", Body(projectId, ownerId, "Budget 50%_draft", due));
        var undatedId = await CreateAsync(ownerClient, "/api/tasks", Body(projectId, peerId, "Budget 50XAdraft", null));
        var tiedId = await CreateAsync(ownerClient, "/api/tasks", Body(projectId, peerId, "Upcoming", due));
        await CreateAsync(outsiderClient, "/api/tasks", Body(foreignProjectId, outsiderId, "SQL private task", due));

        var sorted = await PageAsync(ownerClient, "sort=due&direction=asc");
        Assert.Equal(3, sorted.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { firstId, tiedId, undatedId }, Ids(sorted));
        var literal = await PageAsync(ownerClient, "search=" + Uri.EscapeDataString("50%_") + "&pageSize=1");
        Assert.Equal(1, literal.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { firstId }, Ids(literal));
        var boundary = Uri.EscapeDataString(due.ToString("O", CultureInfo.InvariantCulture));
        Assert.Equal(new[] { firstId, tiedId }, Ids(await PageAsync(ownerClient, $"dueFrom={boundary}&dueTo={boundary}&sort=due&direction=asc")));
        var foreign = await PageAsync(ownerClient, $"projectId={foreignProjectId}&performerId={outsiderId}");
        Assert.Equal(0, foreign.GetProperty("totalCount").GetInt32());
        Assert.Empty(Ids(foreign));

        var original = (await ownerClient.GetFromJsonAsync<JsonElement>($"/api/tasks/{firstId}")).GetProperty("version").GetGuid();
        Assert.NotEqual(Guid.Empty, original);
        var saved = await ownerClient.PutAsJsonAsync($"/api/tasks/{firstId}", Body(projectId, peerId, "Committed SQL edit", due, original));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var current = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetGuid();
        Assert.NotEqual(original, current);
        Assert.Equal(HttpStatusCode.Conflict, (await ownerClient.PutAsJsonAsync($"/api/tasks/{firstId}/reminder", new
        {
            dueDate = due.AddDays(1), reminderEnabled = true, reminderOffsetMinutes = 15, expectedVersion = original
        })).StatusCode);
        ownerClient.DefaultRequestHeaders.Add("If-Match", $"\"{original}\"");
        Assert.Equal(HttpStatusCode.Conflict, (await ownerClient.DeleteAsync($"/api/tasks/{firstId}")).StatusCode);
        ownerClient.DefaultRequestHeaders.Remove("If-Match");
        await factory.WithDatabaseAsync(async first =>
        {
            var firstTask = await first.Tasks.SingleAsync(task => task.Id == firstId);
            await factory.WithDatabaseAsync(async second =>
            {
                var secondTask = await second.Tasks.SingleAsync(task => task.Id == firstId);
                firstTask.Name = "Concurrent SQL winner";
                await first.SaveChangesAsync();
                secondTask.Name = "Concurrent SQL loser";
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            });
        });
        var winner = await ownerClient.GetFromJsonAsync<JsonElement>($"/api/tasks/{firstId}");
        Assert.Equal("Concurrent SQL winner", winner.GetProperty("name").GetString());
        ownerClient.DefaultRequestHeaders.Add("If-Match", $"\"{winner.GetProperty("version").GetGuid()}\"");
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.DeleteAsync($"/api/tasks/{firstId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync($"/api/tasks/{firstId}")).StatusCode);
    }

    private static User Account(string email) => new(email, "SQL", "Task Member", email)
    {
        EmailConfirmed = true, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(),
        RegisteredAt = DateTime.UtcNow, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
    };
    private static object Body(int projectId, int performerId, string name, DateTime? dueDate, Guid? expectedVersion = null) => new
    {
        projectId, performerId, name, description = "Real SQL task lifecycle", state = 0, dueDate, expectedVersion
    };
    private static async Task<int> CreateAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }
    private static async Task<JsonElement> PageAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/tasks/page?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static int[] Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt32()).ToArray();

    private sealed class OwnedTaskDatabase(string serverConnection, string name) : IAsyncDisposable
    {
        private const string Prefix = "taskmanager_task_test_";
        public string ConnectionString { get; } = new SqlConnectionStringBuilder(serverConnection) { InitialCatalog = name }.ConnectionString;
        public static async Task<OwnedTaskDatabase> CreateAsync()
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
                throw new InvalidOperationException("Refusing operations outside the disposable task test database namespace.");
        }
    }
}
