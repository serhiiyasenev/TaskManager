using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DAL.Context;
using DAL.Entities;
using DAL.Enum;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using TaskEntity = DAL.Entities.Task;

namespace Tests.Integration;

public sealed class ThoroughSecurityTests
{
    [Theory]
    [InlineData(101)]
    [InlineData(105)]
    public async System.Threading.Tasks.Task AdministratorDirectory_RequiresCurrentDatabaseRoleDespiteStaleOrUntrustedClaim(int userId)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        await factory.WithDatabaseAsync(async db =>
        {
            db.UserRoles.RemoveRange(await db.UserRoles.Where(role => role.UserId == userId).ToListAsync());
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateUserClient(userId, isAdmin: true);

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task UnhandledPipelineException_PreservesPrivateResponseHeadersAndSafeSerializedProblem()
    {
        using var factory = await DataIsolationFactory.CreateAsync(configureServices: services =>
            services.Configure<MvcOptions>(options => options.Filters.Add(new SyntheticAuthorizationFault())));
        using var client = factory.CreateUserClient();

        var response = await client.GetAsync("/api/auth/csrf");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(500, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("internal_error", body.RootElement.GetProperty("title").GetString());
        Assert.False(body.RootElement.ToString().Contains(SyntheticAuthorizationFault.Marker, StringComparison.Ordinal));
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        Assert.True(response.Headers.TryGetValues("Referrer-Policy", out var values));
        Assert.Equal("no-referrer", Assert.Single(values));
    }

    [Fact]
    public async System.Threading.Tasks.Task StateEndpoint_RealCookieRequiresAntiforgeryAndPermittedOrigin()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();
        var before = await PrepareCookieTaskAsync(factory, client);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");

        var missing = await client.PatchAsJsonAsync("/api/tasks/501/state", new { state = "InProgress", expectedVersion = before.Version });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(before, await ReadStateAsync(factory));
        await RefreshCsrfAsync(client);
        client.DefaultRequestHeaders.Add("Origin", "https://foreign.example.test");
        var foreign = await client.PatchAsJsonAsync("/api/tasks/501/state", new { state = "InProgress", expectedVersion = before.Version });
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(before, await ReadStateAsync(factory));
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        var permitted = await client.PatchAsJsonAsync("/api/tasks/501/state", new { state = "InProgress", expectedVersion = before.Version });
        Assert.Equal(HttpStatusCode.OK, permitted.StatusCode);
        var changed = await ReadStateAsync(factory);
        Assert.Equal(TaskState.InProgress, changed.State);
        Assert.NotEqual(before.Version, changed.Version);
    }

    [Fact]
    public async System.Threading.Tasks.Task StateEndpoint_InvalidExplicitBearerNeverUsesValidIdentityCookie()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();
        var before = await PrepareCookieTaskAsync(factory, client);
        await RefreshCsrfAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-synthetic-bearer");

        var response = await client.PatchAsJsonAsync("/api/tasks/501/state", new { state = "Done", expectedVersion = before.Version });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await ReadStateAsync(factory));
    }

    [Fact]
    public async System.Threading.Tasks.Task StateEndpoint_AnonymousCannotWriteWithValidAntiforgeryAndVersion()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var owner = factory.CreateSecureClient();
        var before = await PrepareCookieTaskAsync(factory, owner);
        using var anonymous = factory.CreateSecureClient();
        await RefreshCsrfAsync(anonymous);

        var response = await anonymous.PatchAsJsonAsync("/api/tasks/501/state", new { state = "Done", expectedVersion = before.Version });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await ReadStateAsync(factory));
    }

    private static async System.Threading.Tasks.Task<TaskStateSnapshot> PrepareCookieTaskAsync(BrowserAuthenticationFactory factory, HttpClient client)
    {
        // Fresh disposable SQLite account and password; no product account,
        // token, secret or Identity error description appears in diagnostics.
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";
        var email = $"security-{Guid.NewGuid():N}@example.test";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskContext>();
            db.Teams.Add(new Team { Id = 501, Name = "Synthetic security team" });
            await db.SaveChangesAsync();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = new User(email, "Synthetic", "Member", email) { EmailConfirmed = true, TeamId = 501 };
            var created = await manager.CreateAsync(user, password);
            Assert.True(created.Succeeded);
            db.Projects.Add(new Project { Id = 501, AuthorId = user.Id, TeamId = 501, Name = "Synthetic security project", Description = "" });
            db.Tasks.Add(new TaskEntity { Id = 501, ProjectId = 501, PerformerId = user.Id, Name = "Synthetic security task", Description = "" });
            await db.SaveChangesAsync();
        }
        await RefreshCsrfAsync(client);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        return await ReadStateAsync(factory);
    }

    private static async System.Threading.Tasks.Task RefreshCsrfAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", value.GetProperty("requestToken").GetString());
    }

    private static async System.Threading.Tasks.Task<TaskStateSnapshot> ReadStateAsync(BrowserAuthenticationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var task = await scope.ServiceProvider.GetRequiredService<TaskContext>().Tasks.AsNoTracking().SingleAsync(task => task.Id == 501);
        return new(task.State, task.Version, task.FinishedAt);
    }
    private sealed record TaskStateSnapshot(TaskState State, Guid Version, DateTime? FinishedAt);
    private sealed class SyntheticAuthorizationFault : IAsyncAuthorizationFilter
    {
        public const string Marker = "synthetic-private-exception-detail";
        public System.Threading.Tasks.Task OnAuthorizationAsync(AuthorizationFilterContext context) =>
            throw new InvalidOperationException(Marker);
    }
}
