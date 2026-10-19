using TaskManager.Hosting;
using TaskManager.Hosting.Cloud;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpsPolicy;
using Yarp.ReverseProxy.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace TaskManager.WebHost;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.AddPrivateFrameworkLogging();
        builder.Services.AddTaskManagerTelemetry(builder.Configuration);
        // Recovery/invitation query tokens must never enter request URL logs.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Warning);
        builder.Logging.AddFilter("Yarp.ReverseProxy", LogLevel.Warning);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Services.AddSingleton(services => GatewayConfiguration.Read(
            services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IHostEnvironment>()));
        builder.Services.AddTrustedProxyForwarding(builder.Configuration);
        builder.Services.AddOptions<HostFilteringOptions>().Configure<GatewayConfiguration>((options, gateway) =>
            options.AllowedHosts = gateway.AllowedOrigins.Select(origin => origin.IdnHost).ToArray());
        builder.Services.AddOptions<HttpsRedirectionOptions>().Configure<GatewayConfiguration>((options, gateway) =>
            options.HttpsPort = gateway.PublicOrigin.Port);
        builder.Services.AddReverseProxy();
        builder.Services.AddHttpClient(GatewayReadinessCheck.ClientName, client => client.Timeout = TimeSpan.FromSeconds(2))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        builder.Services.AddHealthChecks().AddCheck<GatewayReadinessCheck>("critical-api", tags: ["ready"]);
        builder.Services.AddSingleton<IProxyConfigProvider>(services =>
        {
            var gateway = services.GetRequiredService<GatewayConfiguration>();
            return new InMemoryConfigProvider(gateway.Routes, gateway.Clusters);
        });
        var app = builder.Build();
        // Resolve only after the final configuration providers have been applied.
        _ = app.Services.GetRequiredService<GatewayConfiguration>();
        app.UseTrustedProxyForwarding();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            await next(context);
        });
        // Browser routes require HTTPS except explicitly configured Development loopback traffic.
        app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), branch =>
        {
            branch.UseHostFiltering();
            branch.UseWhen(context => !AllowedBrowserOrigins.AllowsDevelopmentHttp(context, app.Configuration, app.Environment),
                secure => secure.UseHttpsRedirection());
        });
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) app.UseHsts();
        app.UseWhen(context => !IsReservedPath(context.Request.Path), branch => branch.UseStaticFiles());
        app.MapReverseProxy();
        var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        var indexPath = Path.Combine(webRoot, "index.html");
        app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("ready") });
        app.MapFallback("/{**path:nonfile}", async context =>
        {
            if (IsReservedPath(context.Request.Path) || !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            if (!File.Exists(indexPath))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.SendFileAsync(indexPath, context.RequestAborted);
        });
        await app.RunAsync();
    }

    private static bool IsReservedPath(PathString path) =>
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/auth", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/chathub", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
}
