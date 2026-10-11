using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebAPI;
using WebAPI.Services;
using Xunit;

namespace Tests.Integration;

public class BrowserAuthenticationTests
{
    [Fact]
    public async Task CsrfBootstrap_ReturnsRequestToken()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();

        var response = await client.GetAsync("/api/auth/csrf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.RequestToken));
    }

    [Theory]
    [InlineData("login")]
    [InlineData("register")]
    public async Task BrowserCredentialMutation_WithoutAntiforgery_IsRejected(string action)
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();

        var response = await client.PostAsJsonAsync($"/api/auth/{action}", new
        {
            userName = "new-member",
            firstName = "New",
            lastName = "Member",
            email = "new-member@example.test",
            password = "TestPassword123!"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutSession_ReturnsUnauthorizedWithoutRedirect()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    private sealed record CsrfResponse(string RequestToken);
}

public sealed class BrowserAuthenticationFactory : WebApplicationFactory<Program>
{
    public HttpClient CreateSecureClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Enabled"] = "false",
                ["Reminders:Enabled"] = "false",
                ["ConnectionStrings:DbConnection"] = "Server=fake;Database=fake;",
                ["Jwt:Key"] = "browser-authentication-test-signing-key",
                ["Jwt:Issuer"] = "TaskManagerAPI",
                ["Jwt:Audience"] = "TaskManagerClients"
            }));
        builder.ConfigureServices(services =>
        {
            var backgroundServices = services.Where(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                (descriptor.ImplementationType == typeof(BootstrapAdminHostedService) ||
                 descriptor.ImplementationType == typeof(TaskReminderScheduler))).ToList();
            foreach (var descriptor in backgroundServices)
            {
                services.Remove(descriptor);
            }
        });
    }
}
