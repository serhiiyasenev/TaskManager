using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TaskManager.WebHost;

internal sealed class GatewayReadinessCheck(
    IWebHostEnvironment environment,
    IHttpClientFactory clients,
    GatewayConfiguration gateway) : IHealthCheck
{
    internal const string ClientName = "GatewayCriticalApi";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        if (!File.Exists(Path.Combine(webRoot, "index.html")))
            return HealthCheckResult.Unhealthy("The SPA entrypoint is unavailable.");

        try
        {
            using var response = await clients.CreateClient(ClientName).GetAsync(
                new Uri(gateway.ApiDestination, "health/ready"), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.StatusCode == HttpStatusCode.OK
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The critical API is unavailable.");
        }
        catch (HttpRequestException)
        {
            return HealthCheckResult.Unhealthy("The critical API is unavailable.");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The critical API probe did not complete.");
        }
    }
}
