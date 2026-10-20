using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Tests.Integration;

public class ReadinessIntegrationTests
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task AzurePrivateHttpProbe_DoesNotRedirectToHttps(string path)
    {
        await using var factory = new ReadinessFactory(HealthCheckResult.Healthy());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task BrowserHttpRequest_StillRequiresHttps()
    {
        await using var factory = new ReadinessFactory(HealthCheckResult.Healthy());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https", response.Headers.Location!.Scheme);
    }
    [Fact]
    public async Task Readiness_WhenSqlUnavailable_Returns503AndRunsSqlCheck()
    {
        await using var factory = new ReadinessFactory(HealthCheckResult.Unhealthy("private database diagnostics"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, factory.SqlChecks);
        Assert.DoesNotContain("private database diagnostics", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_WhenSqlAvailable_Returns200AndRunsSqlCheck()
    {
        await using var factory = new ReadinessFactory(HealthCheckResult.Healthy());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, factory.SqlChecks);
    }

    [Fact]
    public async Task Liveness_WhenSqlUnavailable_Returns200WithoutRunningDependencies()
    {
        await using var factory = new ReadinessFactory(HealthCheckResult.Unhealthy());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.SqlChecks);
    }

    private sealed class ReadinessFactory(HealthCheckResult sqlResult) : WebApiApplicationFactory
    {
        public int SqlChecks;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.Configure<Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionOptions>(options => options.HttpsPort = 443);
                services.PostConfigure<HealthCheckServiceOptions>(options =>
                {
                    // Replace only the dependency: retain production registration tags.
                    var sql = options.Registrations.Single(registration => registration.Name == "sql-server");
                    options.Registrations.Remove(sql);
                    options.Registrations.Add(new HealthCheckRegistration(
                        sql.Name, _ => new StubSqlCheck(this, sqlResult), sql.FailureStatus, sql.Tags, sql.Timeout));
                });
            });
        }
    }

    private sealed class StubSqlCheck(ReadinessFactory owner, HealthCheckResult result) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref owner.SqlChecks);
            return Task.FromResult(result);
        }
    }
}
