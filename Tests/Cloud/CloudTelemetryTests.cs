using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using System.IO.Compression;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TaskManager.Hosting.Cloud;
using Xunit;

namespace Tests.Cloud;

public sealed class CloudTelemetryTests
{
    private const string Secret = "SENTINEL-private-cookie-bearer-notes-title";

    [Fact]
    public void NativeTraceExportDropsRequestSecretsAndPreservesDurationStatusCorrelation()
    {
        using var diagnostics = new NativeMonitorDiagnostics();
        var transport = new CapturingMonitorTransport();
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource("Tests.Cloud.Privacy")
            .SetSampler(new AlwaysOnSampler())
            .SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService("taskmanager-api"))
            .AddProcessor(new PrivateTraceProcessor(new AzureMonitorTraceExporter(ExporterOptions(transport)))).Build();
        using var source = new ActivitySource("Tests.Cloud.Privacy");
        string traceId;
        using (var span = source.StartActivity("POST /api/notes?access_token=" + Secret, ActivityKind.Server))
        {
            Assert.NotNull(span);
            Assert.True(span.Recorded);
            traceId = span.TraceId.ToString();
            span.SetTag("http.request.method", "POST");
            span.SetTag("http.response.status_code", 201);
            foreach (var key in new[] { "url.full", "url.query", "url.path", "http.route", "http.request.header.authorization",
                         "http.request.header.cookie", "access_token", "ai.notes", "task.title", "db.statement", "custom.arbitrary" })
                span.SetTag(key, Secret);
            span.AddBaggage("user.email", Secret);
            span.TraceStateString = "private=" + Secret;
            span.SetStatus(ActivityStatusCode.Error, Secret);
        }
        Assert.True(provider.ForceFlush());
        var exported = string.Join("\n", transport.Bodies);
        Assert.True(!string.IsNullOrEmpty(exported), diagnostics.Summary);
        Assert.Contains("RequestData", exported);
        Assert.Contains(traceId, exported);
        Assert.Contains("201", exported);
        Assert.DoesNotContain(Secret, exported);
        Assert.DoesNotContain("db.statement", exported);
    }

    [Fact]
    public void NativeExporterNeverReceivesArbitrarySpanEventsOrExceptionContent()
    {
        using var diagnostics = new NativeMonitorDiagnostics();
        var transport = new CapturingMonitorTransport();
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource("Tests.Cloud.Events")
            .SetSampler(new AlwaysOnSampler())
            .SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService("taskmanager-api"))
            .AddProcessor(new PrivateTraceProcessor(new AzureMonitorTraceExporter(ExporterOptions(transport)))).Build();
        using var source = new ActivitySource("Tests.Cloud.Events");
        using (var unsafeSpan = source.StartActivity("private-event", ActivityKind.Server))
        {
            Assert.NotNull(unsafeSpan);
            Assert.True(unsafeSpan.Recorded);
            unsafeSpan.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                ["exception.type"] = "SyntheticException", ["exception.message"] = Secret,
                ["exception.stacktrace"] = Secret, ["task.title"] = Secret
            }));
        }
        using (var safeSpan = source.StartActivity("safe-request", ActivityKind.Server))
        {
            Assert.NotNull(safeSpan);
            Assert.True(safeSpan.Recorded);
            safeSpan.SetTag("http.response.status_code", 204);
        }
        Assert.True(provider.ForceFlush());
        var exported = string.Join("\n", transport.Bodies);
        Assert.True(!string.IsNullOrEmpty(exported), diagnostics.Summary);
        Assert.Contains("RequestData", exported);
        Assert.DoesNotContain("ExceptionData", exported);
        Assert.DoesNotContain(Secret, exported);
        Assert.DoesNotContain("private-event", exported);
    }

    [Fact]
    public void NativeMetricExportPreservesAiCountsAndLatencyWithoutUserContentOrDimensions()
    {
        var transport = new CapturingMonitorTransport();
        using var reader = new PeriodicExportingMetricReader(new AzureMonitorMetricExporter(ExporterOptions(transport)), 60_000);
        using var provider = CloudTelemetry.ConfigurePrivateMetrics(Sdk.CreateMeterProviderBuilder())
            .SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService("taskmanager-api")).AddReader(reader).Build();
        using var meter = new Meter("TaskManager.AI");
        meter.CreateCounter<long>("ai.accepted_items").Add(2, new KeyValuePair<string, object?>("task.title", Secret));
        meter.CreateHistogram<double>("ai.inference_duration", "ms").Record(125,
            new KeyValuePair<string, object?>("user.id", Secret));
        Assert.True(provider.ForceFlush());
        var exported = string.Join("\n", transport.Bodies);
        Assert.Contains("ai.accepted_items", exported);
        Assert.Contains("ai.inference_duration", exported);
        Assert.DoesNotContain(Secret, exported);
        Assert.DoesNotContain("task.title", exported);
        Assert.DoesNotContain("user.id", exported);
    }

    [Theory]
    [InlineData("Disabled")]
    [InlineData("AzureMonitor")]
    public void ProviderRegistrationNeverAddsRawOtelLogExport(string providerName)
    {
        var transport = new CapturingMonitorTransport();
        var exporters = new NativeMonitorFixture(transport);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICloudTelemetryExporters>(exporters);
        services.AddTaskManagerTelemetry(Config(providerName));
        using var provider = services.BuildServiceProvider();
        _ = provider.GetService<TracerProvider>();
        _ = provider.GetService<MeterProvider>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Synthetic.Business");
        using (logger.BeginScope(new Dictionary<string, object> { ["Authorization"] = Secret }))
            logger.LogWarning(new InvalidOperationException(Secret), "Task title and notes {Content}", Secret);
        Assert.DoesNotContain(provider.GetServices<ILoggerProvider>(), p => p.GetType().FullName?.Contains("OpenTelemetry", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(Secret, string.Join("\n", transport.Bodies));
        Assert.Equal(providerName == "Disabled" ? 0 : 2, exporters.ClientCount);
    }

    [Theory]
    [InlineData("Telemetry:Provider", "unknown")]
    [InlineData("Telemetry:TracesPerSecond", "0")]
    [InlineData("Telemetry:TracesPerSecond", "6")]
    [InlineData("Telemetry:ManagedIdentityClientId", "not-a-guid")]
    [InlineData("Telemetry:ServiceName", "private-title")]
    [InlineData("Telemetry:ConnectionString", "InstrumentationKey=not-a-guid;IngestionEndpoint=http://foreign.example.test/?token=secret")]
    public void InvalidTelemetryConfigurationDoesNotCreateExporter(string key, string value)
    {
        var exporters = new NativeMonitorFixture(new CapturingMonitorTransport());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICloudTelemetryExporters>(exporters);
        var error = Record.Exception(() =>
        {
            services.AddTaskManagerTelemetry(Config("AzureMonitor", new() { [key] = value }));
            using var provider = services.BuildServiceProvider();
            _ = provider.GetService<TracerProvider>();
        });
        Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Equal(0, exporters.ClientCount);
    }

    [Fact]
    public void IncomingSampledParentCannotBypassProductTraceRateLimit()
    {
        var clock = new TelemetryClock();
        var captured = new CounterActivityExporter();
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource("Tests.Cloud.Budget")
            .SetSampler(new BoundedTraceSampler(1, clock)).AddProcessor(new SimpleActivityExportProcessor(captured)).Build();
        using var source = new ActivitySource("Tests.Cloud.Budget");
        var remote = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded, isRemote: true);
        for (var index = 0; index < 40; index++) source.StartActivity("request", ActivityKind.Server, remote)?.Dispose();
        Assert.Equal(1, captured.Count);
        clock.Now += TimeSpan.FromSeconds(1);
        source.StartActivity("request", ActivityKind.Server, remote)?.Dispose();
        Assert.Equal(2, captured.Count);
    }

    internal static AzureMonitorExporterOptions ExporterOptions(CapturingMonitorTransport transport) => new()
    {
        ConnectionString = $"InstrumentationKey={transport.InstrumentationKey:D};IngestionEndpoint=https://westeurope-0.in.applicationinsights.azure.com/",
        Credential = new SyntheticMonitorCredential(), Transport = transport,
        DisableOfflineStorage = true, EnableLiveMetrics = false
    };

    private static IConfiguration Config(string name, Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Telemetry:Provider"] = name,
            ["Telemetry:ConnectionString"] = "InstrumentationKey=11111111-1111-4111-8111-111111111111;IngestionEndpoint=https://westeurope-0.in.applicationinsights.azure.com/",
            ["Telemetry:ServiceName"] = "taskmanager-api", ["Telemetry:TracesPerSecond"] = "1"
        };
        foreach (var pair in overrides ?? []) values[pair.Key] = pair.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class TelemetryClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CounterActivityExporter : BaseExporter<Activity>
    {
        public long Count { get; private set; }
        public override ExportResult Export(in Batch<Activity> batch) { Count += batch.Count; return ExportResult.Success; }
    }
}

internal sealed class SyntheticMonitorCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken ct) =>
        new("synthetic-monitor-mi-token", DateTimeOffset.UtcNow.AddHours(1));
    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken ct) => new(GetToken(requestContext, ct));
}

internal sealed class NativeMonitorFixture(CapturingMonitorTransport transport) : ICloudTelemetryExporters
{
    public int ClientCount { get; private set; }
    public BaseExporter<Activity> CreateTraceExporter(CloudTelemetryOptions options)
    {
        ClientCount++; return new AzureMonitorTraceExporter(CloudTelemetryTests.ExporterOptions(transport));
    }
    public BaseExporter<Metric> CreateMetricExporter(CloudTelemetryOptions options)
    {
        ClientCount++; return new AzureMonitorMetricExporter(CloudTelemetryTests.ExporterOptions(transport));
    }
}

internal sealed class CapturingMonitorTransport : HttpPipelineTransport
{
    // The native SDK transmitter cache is keyed only by connection string.
    // Signals in this fixture share a key; independent fixtures must not share it.
    public Guid InstrumentationKey { get; } = Guid.NewGuid();
    public ConcurrentQueue<string> Bodies { get; } = new();
    public override Request CreateRequest() => HttpClientTransport.Shared.CreateRequest();
    public override void Process(HttpMessage message)
    {
        using var stream = new MemoryStream();
        message.Request.Content?.WriteTo(stream, message.CancellationToken);
        var bytes = stream.ToArray();
        if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            using var compressed = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var text = new StreamReader(compressed);
            Bodies.Enqueue(text.ReadToEnd());
        }
        else Bodies.Enqueue(Encoding.UTF8.GetString(bytes));
        message.Response = new NativeBlobResponse(200, "synthetic")
        {
            ContentStream = new MemoryStream(Encoding.UTF8.GetBytes("{\"itemsReceived\":1,\"itemsAccepted\":1,\"errors\":[]}"))
        };
    }
    public override ValueTask ProcessAsync(HttpMessage message) { Process(message); return ValueTask.CompletedTask; }
}

internal sealed class NativeMonitorDiagnostics : EventListener
{
    private readonly ConcurrentQueue<string> _errors = new();
    public string Summary => "Native SDK export diagnostic: " + string.Join(" | ", _errors);
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name.StartsWith("Azure-Monitor", StringComparison.Ordinal) || eventSource.Name == "OpenTelemetry-Sdk")
            EnableEvents(eventSource, EventLevel.Verbose);
    }
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.Level is not (EventLevel.Error or EventLevel.Warning)) return;
        _errors?.Enqueue(eventData.EventSource.Name + ":" + eventData.EventName + " " +
            string.Join(";", eventData.Payload?.Select(value => value?.ToString()) ?? []));
    }
}
