using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using BLL.Interfaces;
using DAL.Context;
using Project = DAL.Entities.Project;
using Team = DAL.Entities.Team;
using User = DAL.Entities.User;
using DAL.Enum;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using TaskEntity = DAL.Entities.Task;

namespace Tests.Integration;

public class DataIsolationTests
{
    [Theory]
    [InlineData("/api/projects")]
    [InlineData("/api/tasks")]
    [InlineData("/api/teams")]
    [InlineData("/api/users/101")]
    [InlineData("/api/analytics/GetProjectsInfo")]
    public async Task Anonymous_ProtectedLegacyReads_RequireAuthentication(string route)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
    }

    [Theory]
    [InlineData("/api/projects", new[] { 101, 103 })]
    [InlineData("/api/tasks", new[] { 101, 103 })]
    [InlineData("/api/teams", new[] { 101 })]
    public async Task Member_Collections_ContainOnlyCurrentTeam(string route, int[] expectedIds)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        var response = await client.GetAsync(route);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedIds, json.RootElement.EnumerateArray().Select(item => item.GetProperty("id").GetInt32()).Order().ToArray());
    }

    [Theory]
    [InlineData("/api/projects/102")]
    [InlineData("/api/projects/104")]
    [InlineData("/api/tasks/102")]
    [InlineData("/api/tasks/104")]
    [InlineData("/api/teams/102")]
    [InlineData("/api/users/103")]
    public async Task Member_ForeignRecords_AreNotVisibleEvenForFormerAuthorOrAssignee(string route)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
    }

    [Theory]
    [InlineData("/api/projects/102")]
    [InlineData("/api/tasks/102")]
    [InlineData("/api/teams/102")]
    [InlineData("/api/users/103")]
    public async Task Admin_CanReadForeignRecords(string route)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(105, isAdmin: true);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task MembershipRevoked_ExistingPrincipalImmediatelyLosesTeamAccess()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/projects/101")).StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            (await db.Users.SingleAsync(user => user.Id == 101)).TeamId = null;
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projects/101")).StatusCode);
        var response = await client.GetAsync("/api/tasks");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task AdminRoleRevoked_StalePrincipalCannotReadForeignDataOrGlobalAnalytics()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(105, isAdmin: true);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/projects/102")).StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            db.UserRoles.RemoveRange(await db.UserRoles.Where(role => role.UserId == 105).ToListAsync());
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projects/102")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/analytics/GetProjectsInfo")).StatusCode);
    }

    [Fact]
    public async Task AdminClaimWithoutPersistedRole_DoesNotElevateMember()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101, isAdmin: true);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/projects/102")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/analytics/GetProjectsInfo")).StatusCode);
    }

    [Fact]
    public async Task PrincipalForMissingUser_DoesNotGainAccess()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(999);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/projects")).StatusCode);
    }

    [Fact]
    public async Task OwnUserDetails_DoNotLeakTasksFromFormerTeam()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        var response = await client.GetAsync("/api/users/101");
        response.EnsureSuccessStatusCode();
        Assert.DoesNotContain("Former team private task", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProjectCreation_UsesCallerAndIgnoresSuppliedEntityGraphs()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        var usersBefore = 0;
        var teamsBefore = 0;
        await factory.WithDatabaseAsync(async db => { usersBefore = await db.Users.CountAsync(); teamsBefore = await db.Teams.CountAsync(); });
        var body = ProjectBody(101, 103, "Created safely");
        var response = await client.PostAsJsonAsync("/api/projects", body);
        response.EnsureSuccessStatusCode();
        await factory.WithDatabaseAsync(async db =>
        {
            var created = await db.Projects.SingleAsync(project => project.Name == "Created safely");
            Assert.Equal(101, created.AuthorId);
            Assert.Equal(101, created.TeamId);
            Assert.Equal(usersBefore, await db.Users.CountAsync());
            Assert.Equal(teamsBefore, await db.Teams.CountAsync());
        });
    }

    [Fact]
    public async Task Peer_CannotEditProjectAuthoredByAnotherMember()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(102);
        var response = await client.PutAsJsonAsync("/api/projects/101", ProjectBody(101, 102));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.Equal(101, (await db.Projects.SingleAsync(project => project.Id == 101)).AuthorId));
    }

    [Fact]
    public async Task Author_ProjectUpdateCannotTransferAuthorship()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        var response = await client.PutAsJsonAsync("/api/projects/101", ProjectBody(101, 102));
        response.EnsureSuccessStatusCode();
        await factory.WithDatabaseAsync(async db => Assert.Equal(101, (await db.Projects.SingleAsync(project => project.Id == 101)).AuthorId));
    }

    [Fact]
    public async Task Member_CannotAssignTaskToForeignTeamUser()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        var response = await client.PutAsJsonAsync("/api/tasks/101", TaskBody(101, 103));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.Equal(101, (await db.Tasks.SingleAsync(task => task.Id == 101)).PerformerId));
    }

    [Fact]
    public async Task Member_CannotMoveTaskToForeignTeamProject()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        var response = await client.PutAsJsonAsync("/api/tasks/101", TaskBody(102, 103));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.Equal(101, (await db.Tasks.SingleAsync(task => task.Id == 101)).ProjectId));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("reminder")]
    [InlineData("delete")]
    [InlineData("executed")]
    public async Task Member_ForeignTaskMutations_AreRejected(string operation)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        var response = operation switch
        {
            "update" => await client.PutAsJsonAsync("/api/tasks/102", TaskBody(102, 103)),
            "reminder" => await client.PutAsJsonAsync("/api/tasks/102/reminder", new { dueDate = DateTime.UtcNow.AddDays(3), reminderEnabled = true, reminderOffsetMinutes = 15 }),
            "delete" => await client.DeleteAsync("/api/tasks/102"),
            _ => await client.PostAsJsonAsync("/api/tasks/AddExecutedTask", new { taskId = 102, taskName = "spoofed completion" })
        };
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.True(await db.Tasks.AnyAsync(task => task.Id == 102));
            Assert.False(await db.ExecutedTasks.AnyAsync(task => task.TaskId == 102));
        });
    }

    [Fact]
    public async Task Member_CannotChangeOwnTeamThroughProfileUpdate()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        var response = await client.PutAsJsonAsync("/api/users/101", new { teamId = 102, email = "member101@example.test", userName = "member101", firstName = "Member", lastName = "One", birthDay = "1990-01-01" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.Equal(101, (await db.Users.SingleAsync(user => user.Id == 101)).TeamId));
    }

    [Fact]
    public async Task Member_CannotChangeConfirmedEmailThroughProfileUpdate()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        var response = await client.PutAsJsonAsync("/api/users/101", new { teamId = 101, email = "unconfirmed-change@example.test", userName = "member101", firstName = "Member", lastName = "One", birthDay = "1990-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.Equal("member101@example.test", (await db.Users.SingleAsync(user => user.Id == 101)).Email));
    }

    [Fact]
    public async Task Member_CannotDeleteAnotherUser()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/api/users/104")).StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.True(await db.Users.AnyAsync(user => user.Id == 104)));
    }

    [Fact]
    public async Task Member_CannotUpdateAnotherUsersProfile()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        var response = await client.PutAsJsonAsync("/api/users/102", new { teamId = 101, email = "member102@example.test", userName = "member102", firstName = "Hacked", lastName = "Peer", birthDay = "1990-01-01" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Member_CannotRenameTeam()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/teams/101", new { name = "Unauthorized rename" })).StatusCode);
    }

    [Fact]
    public async Task Member_GlobalAnalytics_RequireAdministrator()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/analytics/GetProjectsInfo")).StatusCode);
    }

    private static Project ProjectBody(int teamId, int authorId, string name = "Updated project") => new()
    {
        TeamId = teamId, AuthorId = authorId, Name = name, Description = "Valid request", Deadline = DateTime.UtcNow.AddDays(10),
        Author = UserGraph("Injected author"), Team = new Team { Name = "Injected team", Users = [], Projects = [] }, Tasks = []
    };

    private static TaskEntity TaskBody(int projectId, int performerId) => new()
    {
        ProjectId = projectId, PerformerId = performerId, Name = "Updated task", Description = "Valid request", State = TaskState.ToDo,
        Project = ProjectBody(101, 101, "Injected project"), Performer = UserGraph("Injected performer")
    };

    private static User UserGraph(string name) => new()
    {
        UserName = name.Replace(" ", ""), FirstName = name, LastName = "Graph", Email = "graph@example.test", Tasks = []
    };
}

internal sealed class DataIsolationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection? _connection;
    private readonly IInterceptor? _interceptor;
    private readonly string? _sqlConnection;

    private DataIsolationFactory(IInterceptor? interceptor = null, string? sqlConnection = null)
    {
        _interceptor = interceptor;
        _sqlConnection = sqlConnection;
        if (sqlConnection is null)
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
        }
    }

    public static async Task<DataIsolationFactory> CreateAsync(IInterceptor? interceptor = null, string? sqlConnection = null)
    {
        var factory = new DataIsolationFactory(interceptor, sqlConnection);
        await factory.WithDatabaseAsync(async db =>
        {
            await db.Database.EnsureCreatedAsync();
            // The SQL Server lifecycle owns a disposable empty database and
            // creates actors using generated identity keys. SQLite fixtures retain
            // their deterministic IDs for resource authorization scenarios.
            if (sqlConnection is not null) return;
            db.Roles.Add(new IdentityRole<int> { Id = 101, Name = "admin", NormalizedName = "ADMIN", ConcurrencyStamp = Guid.NewGuid().ToString() });
            db.Teams.AddRange(new Team { Id = 101, Name = "Isolation team A" }, new Team { Id = 102, Name = "Isolation team B" });
            db.Users.AddRange(Enumerable.Range(101, 5).Select(id => new User
            {
                Id = id, UserName = $"member{id}", NormalizedUserName = $"MEMBER{id}", Email = $"member{id}@example.test",
                NormalizedEmail = $"MEMBER{id}@EXAMPLE.TEST", FirstName = "Member", LastName = id.ToString(), EmailConfirmed = true,
                TeamId = id is 101 or 102 ? 101 : id == 103 ? 102 : null,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            }));
            db.Projects.AddRange(
                new Project { Id = 101, AuthorId = 101, TeamId = 101, Name = "Team A project", Description = "A" },
                new Project { Id = 102, AuthorId = 103, TeamId = 102, Name = "Team B private project", Description = "B" },
                new Project { Id = 103, AuthorId = 102, TeamId = 101, Name = "Peer authored project", Description = "A" },
                new Project { Id = 104, AuthorId = 101, TeamId = 102, Name = "Former team private project", Description = "B" });
            db.Tasks.AddRange(
                new TaskEntity { Id = 101, ProjectId = 101, PerformerId = 101, Name = "Team A task", Description = "A" },
                new TaskEntity { Id = 102, ProjectId = 102, PerformerId = 103, Name = "Team B private task", Description = "B" },
                new TaskEntity { Id = 103, ProjectId = 101, PerformerId = 102, Name = "Peer assigned task", Description = "A" },
                new TaskEntity { Id = 104, ProjectId = 104, PerformerId = 101, Name = "Former team private task", Description = "B" });
            db.UserRoles.Add(new IdentityUserRole<int> { UserId = 105, RoleId = 101 });
            await db.SaveChangesAsync();
        });
        return factory;
    }

    public HttpClient CreateUserClient(int? userId = null, bool isAdmin = false)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (userId.HasValue)
        {
            client.DefaultRequestHeaders.Add("X-Isolation-User", userId.Value.ToString());
            // This factory authenticates through its test-only scheme; marking
            // requests as bearer isolates resource authorization from browser CSRF.
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "isolation-test-principal");
        }
        if (isAdmin) client.DefaultRequestHeaders.Add("X-Isolation-Admin", "true");
        return client;
    }

    public async Task WithDatabaseAsync(Func<TaskContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<TaskContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BootstrapAdmin:Enabled"] = "false", ["Reminders:Enabled"] = "false", ["ConnectionStrings:DbConnection"] = _sqlConnection ?? "Server=metadata-only;Database=unused",
            ["Jwt:Key"] = "isolation-test-key-with-at-least-thirty-two-characters"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<TaskContext>();
            services.RemoveAll<DbContextOptions<TaskContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<TaskContext>>();
            services.AddDbContext<TaskContext>(options =>
            {
                if (_sqlConnection is null) options.UseSqlite(_connection!);
                else options.UseSqlServer(_sqlConnection);
                if (_interceptor is not null) options.AddInterceptors(_interceptor);
            });
            services.RemoveAll<IQueueService>();
            services.AddSingleton<IQueueService, IsolationQueue>();
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = IsolationAuthenticationHandler.SchemeName;
                options.DefaultAuthenticateScheme = IsolationAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = IsolationAuthenticationHandler.SchemeName;
                options.DefaultForbidScheme = IsolationAuthenticationHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, IsolationAuthenticationHandler>(IsolationAuthenticationHandler.SchemeName, _ => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection?.Dispose();
    }

    private sealed class IsolationQueue : IQueueService
    {
        public Task<bool> PostValue(string message, string? queueName = null, CancellationToken ct = default) => Task.FromResult(true);
    }
}

internal sealed class IsolationAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "IsolationTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!int.TryParse(Request.Headers["X-Isolation-User"], out var id)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id.ToString()), new("team_id", "101") };
        if (Request.Headers["X-Isolation-Admin"] == "true") claims.Add(new Claim(ClaimTypes.Role, "admin"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
    }
}
