using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TaskManager.Hosting.Cloud;

public sealed class CloudTelemetryOptions
{
    public string Provider { get; set; } = "Disabled";
    public string? ConnectionString { get; set; }
    public string? ManagedIdentityClientId { get; set; }
    public string ServiceName { get; set; } = "taskmanager-api";
    public double TracesPerSecond { get; set; } = 1;
}

public static class CloudTelemetry
{
    public static IServiceCollection AddTaskManagerTelemetry(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CloudTelemetryOptions>().Bind(configuration.GetSection("Telemetry"))
            .Configure(Validate).ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICloudTelemetryExporters, AzureTelemetryExporters>();
        // Register explicit pipelines. The distro also subscribes to content-bearing
        // Azure/SQL spans, forwards logs, and disables URL query redaction defaults.
        services.AddOpenTelemetry().WithTracing(builder => builder.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
            .WithMetrics(builder => ConfigurePrivateMetrics(builder));
        services.ConfigureOpenTelemetryTracerProvider((sp, builder) =>
        {
            var options = sp.GetRequiredService<IOptions<CloudTelemetryOptions>>().Value;
            if (options.Provider == "Disabled")
            {
                builder.SetSampler(new AlwaysOffSampler());
                return;
            }
            builder.SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService(options.ServiceName))
                .SetSampler(new BoundedTraceSampler(options.TracesPerSecond, sp.GetRequiredService<TimeProvider>()))
                .AddProcessor(new PrivateTraceProcessor(sp.GetRequiredService<ICloudTelemetryExporters>().CreateTraceExporter(options)));
        });
        services.ConfigureOpenTelemetryMeterProvider((sp, builder) =>
        {
            var options = sp.GetRequiredService<IOptions<CloudTelemetryOptions>>().Value;
            if (options.Provider == "Disabled") return;
            builder.SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService(options.ServiceName))
                .AddReader(new PeriodicExportingMetricReader(
                    sp.GetRequiredService<ICloudTelemetryExporters>().CreateMetricExporter(options), 60_000));
        });
        // OTel logs are intentionally absent: ACA collects separately sanitized console logs.
        return services;
    }

    public static MeterProviderBuilder ConfigurePrivateMetrics(MeterProviderBuilder builder) => builder
        .AddMeter("TaskManager.AI", "Microsoft.AspNetCore.Hosting", "System.Net.Http")
        .AddView("*", new MetricStreamConfiguration { TagKeys = [] });

    private static void Validate(CloudTelemetryOptions options)
    {
        if (options.Provider is not ("Disabled" or "AzureMonitor") ||
            options.ServiceName is not ("taskmanager-web" or "taskmanager-api" or "taskmanager-notifier") ||
            !double.IsFinite(options.TracesPerSecond) || options.TracesPerSecond is < 0.1 or > 5)
            throw new InvalidOperationException("Telemetry provider, service name or trace limit is invalid.");
        CloudManagedIdentity.ValidateClientId(options.ManagedIdentityClientId);
        if (options.Provider == "Disabled") return;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in (options.ConnectionString ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !fields.TryAdd(parts[0], parts[1]) ||
                !new[] { "InstrumentationKey", "IngestionEndpoint", "LiveEndpoint", "ApplicationId" }.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Telemetry:ConnectionString must identify an explicit Azure Monitor resource.");
        }
        if (!fields.TryGetValue("InstrumentationKey", out var key) || !Guid.TryParse(key, out var id) || id == Guid.Empty ||
            !fields.TryGetValue("IngestionEndpoint", out var endpoint) || !HasAzureEndpoint(endpoint, false) ||
            (fields.TryGetValue("LiveEndpoint", out var live) && !HasAzureEndpoint(live, true)) ||
            (fields.TryGetValue("ApplicationId", out var appId) && !Guid.TryParse(appId, out _)))
            throw new InvalidOperationException("Telemetry:ConnectionString must use HTTPS Azure Monitor endpoints without credentials or query.");
    }

    private static bool HasAzureEndpoint(string value, bool live)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            return false;
        return live ? uri.IdnHost.EndsWith(".livediagnostics.monitor.azure.com", StringComparison.OrdinalIgnoreCase)
            : uri.IdnHost.EndsWith(".in.applicationinsights.azure.com", StringComparison.OrdinalIgnoreCase) ||
              uri.IdnHost is "dc.services.visualstudio.com" or "dc.applicationinsights.azure.com";
    }
}

public interface ICloudTelemetryExporters
{
    BaseExporter<Activity> CreateTraceExporter(CloudTelemetryOptions options);
    BaseExporter<Metric> CreateMetricExporter(CloudTelemetryOptions options);
}

public sealed class PrivateTraceProcessor(BaseExporter<Activity> exporter) : BatchActivityExportProcessor(exporter)
{
    public override void OnEnd(Activity data)
    {
        // Activity events are immutable via public APIs and may contain exception
        // bodies. Omit such spans rather than allowing the native exporter to serialize them.
        if (data.Events.Any()) return;
        foreach (var tag in data.TagObjects.ToArray())
        {
            var method = tag.Key is "http.method" or "http.request.method" && tag.Value is string verb &&
                verb is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" or "TRACE" or "CONNECT";
            var status = tag.Key is "http.status_code" or "http.response.status_code" && tag.Value is int code && code is >= 100 and <= 599;
            if (!method && !status) data.SetTag(tag.Key, null);
        }
        foreach (var baggage in data.Baggage.ToArray()) data.SetBaggage(baggage.Key, null);
        data.TraceStateString = null;
        data.SetStatus(data.Status);
        data.DisplayName = data.Kind == ActivityKind.Server ? "HTTP request" : "HTTP dependency";
        base.OnEnd(data);
    }
}

public sealed class BoundedTraceSampler : Sampler
{
    private readonly object _gate = new();
    private readonly double _rate;
    private readonly TimeProvider _clock;
    private double _tokens = 1;
    private DateTimeOffset _updated;

    public BoundedTraceSampler(double tracesPerSecond, TimeProvider clock)
    {
        if (!double.IsFinite(tracesPerSecond) || tracesPerSecond is < 0.1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(tracesPerSecond));
        _rate = tracesPerSecond;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _updated = clock.GetUtcNow();
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            var elapsed = Math.Max(0, (now - _updated).TotalSeconds);
            if (now > _updated) _updated = now;
            _tokens = Math.Min(1, _tokens + elapsed * _rate);
            if (_tokens < 1) return new SamplingResult(SamplingDecision.Drop);
            _tokens -= 1;
            return new SamplingResult(SamplingDecision.RecordAndSample);
        }
    }
}

internal sealed class AzureTelemetryExporters : ICloudTelemetryExporters
{
    public BaseExporter<Activity> CreateTraceExporter(CloudTelemetryOptions options) => new AzureMonitorTraceExporter(CreateOptions(options));
    public BaseExporter<Metric> CreateMetricExporter(CloudTelemetryOptions options) => new AzureMonitorMetricExporter(CreateOptions(options));

    private static AzureMonitorExporterOptions CreateOptions(CloudTelemetryOptions options)
    {
        var exporter = new AzureMonitorExporterOptions
        {
            ConnectionString = options.ConnectionString, Credential = CloudManagedIdentity.Create(options.ManagedIdentityClientId),
            TracesPerSecond = options.TracesPerSecond, DisableOfflineStorage = true, EnableLiveMetrics = false,
            EnableStandardMetrics = false, EnablePerformanceCounters = false
        };
        exporter.Diagnostics.IsLoggingEnabled = false;
        exporter.Diagnostics.IsLoggingContentEnabled = false;
        return exporter;
    }
}
