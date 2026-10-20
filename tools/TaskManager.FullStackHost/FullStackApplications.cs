extern alias NotifierLib;

using System.Net;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Services.AI;
using BLL.Services.Notifications;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotifierEntry = NotifierLib::Notifier.Program;
using NotifierListener = NotifierLib::Notifier.RabbitMqListenerService;
using ActualRealtimePublisher = NotifierLib::Notifier.Notifications.InboxRealtimePublisher;
using ApiEntry = global::Program;
using GatewayEntry = TaskManager.WebHost.Program;
using System.Security.Cryptography.X509Certificates;

namespace TaskManager.FullStackHost;

internal sealed class FullStackApplications(FullStackSettings settings, FullStackDatabase database,
    FullStackClock clock, FullStackMail mail, FullStackAiMode aiMode, ControlledQueue controlled,
    SettlementEvidence evidence, Uri publicOrigin, Uri apiOrigin, Uri notifierOrigin)
{
    public WebApplicationFactory<ApiEntry>? Api { get; private set; }
    public WebApplicationFactory<NotifierEntry>? Notifier { get; private set; }
    public WebApplicationFactory<GatewayEntry>? Gateway { get; private set; }
    public List<object> StopFailures { get; } = [];

    public object GetBindingDiagnostics() => new
    {
        apiExpectedPort = apiOrigin.Port, apiActualPort = Api?.ClientOptions.BaseAddress.Port,
        notifierExpectedPort = notifierOrigin.Port, notifierActualPort = Notifier?.ClientOptions.BaseAddress.Port,
        gatewayExpectedPort = publicOrigin.Port, gatewayActualPort = Gateway?.ClientOptions.BaseAddress.Port,
        gatewayHttps = Gateway?.ClientOptions.BaseAddress.Scheme == "https"
    };

    public void SetEarlyProcessConfiguration()
    {
        // Actual Programs read health checks/transport/proxy selection before
        // deferred factory callbacks. This isolated child has no product process.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");
        foreach (var key in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(value =>
            value.StartsWith("BrowserAuth__AdditionalAllowedOrigins__", StringComparison.OrdinalIgnoreCase)).ToArray())
            Environment.SetEnvironmentVariable(key, null);
        foreach (var (key, value) in Configuration())
            Environment.SetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal), value);
    }

    public void StartApi(ReservedPort port)
    {
        Api = new ActualFactory<ApiEntry>(Path.Combine(settings.RepositoryRoot, "src", "backend", "WebAPI"), Configuration(), services =>
        {
            ConfigureClock(services);
            RemoveOwnedWorkers(services, notifier: false);
            services.RemoveAll<IAccountEmailSender>();
            services.AddSingleton<IAccountEmailSender>(mail);
            services.RemoveAll<ITaskExtractionChatTransport>();
            services.AddSingleton<ITaskExtractionChatTransport>(new FullStackAiTransport(aiMode));
            services.RemoveAll<ITaskExtractionProvider>();
            services.AddSingleton<ITaskExtractionProvider>(provider => new ModeAwareExtractionProvider(
                new AzureTaskExtractionProvider(provider.GetRequiredService<IOptions<TaskExtractionOptions>>().Value,
                    provider.GetRequiredService<IHostEnvironment>(), provider.GetRequiredService<ITaskExtractionChatTransport>()), aiMode));
            if (settings.Transport == "Controlled")
            {
                services.RemoveAll<IQueueService>();
                services.AddSingleton<IQueueService>(controlled);
            }
        });
        Api.UseKestrel(options => options.Listen(IPAddress.Loopback, port.Port));
        port.Dispose();
        Api.StartServer();
    }

    public void StartNotifier(ReservedPort port)
    {
        Notifier = new ActualFactory<NotifierEntry>(Path.Combine(settings.RepositoryRoot, "src", "backend", "Notifier"), Configuration(), services =>
        {
            ConfigureClock(services);
            RemoveOwnedWorkers(services, notifier: true);
            services.RemoveAll<IInboxRealtimePublisher>();
            services.AddSingleton<ActualRealtimePublisher>();
            services.AddSingleton<IInboxRealtimePublisher>(provider => new ObservedRealtimePublisher(
                provider.GetRequiredService<ActualRealtimePublisher>(), evidence));
            if (settings.Transport == "Controlled")
            {
                services.RemoveAll<IQueueService>();
                services.AddSingleton<IQueueService>(controlled);
                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    var broker = options.Registrations.Single(value => value.Name == "notification-broker");
                    options.Registrations.Remove(broker);
                    options.Registrations.Add(new HealthCheckRegistration("notification-broker",
                        _ => new ControlledBrokerHealth(controlled), HealthStatus.Unhealthy, ["ready"]));
                });
            }
        });
        Notifier.UseKestrel(options => options.Listen(IPAddress.Loopback, port.Port));
        port.Dispose();
        Notifier.StartServer();
        if (settings.Transport == "Controlled") controlled.Bind(Notifier.Services.GetRequiredService<NotificationDeliveryCoordinator>());
    }

    public void StartGateway(ReservedPort port, X509Certificate2 certificate)
    {
        Gateway = new ActualFactory<GatewayEntry>(Path.Combine(settings.RepositoryRoot, "src", "backend", "WebHost"), Configuration(), _ => { },
            Path.Combine(settings.RepositoryRoot, "src", "frontend", "dist"));
        Gateway.UseKestrel(options => options.Listen(IPAddress.Loopback, port.Port, endpoint => endpoint.UseHttps(certificate)));
        port.Dispose();
        Gateway.StartServer();
    }

    public async Task DispatchAsync(CancellationToken ct)
    {
        if (Notifier is null) throw new InvalidOperationException("Notification host is unavailable.");
        var dispatcher = Notifier.Services.GetRequiredService<ITaskNotificationDispatcher>();
        await using var db = database.CreateContext();
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(db.NotificationOutbox
            .Where(value => value.Status == DAL.Entities.NotificationOutboxStatus.Pending
                && (value.LockedUntilUtc == null || value.LockedUntilUtc <= now))
            .OrderBy(value => value.CreatedAtUtc).ThenBy(value => value.Id).Select(value => value.Id).Take(100), ct);
        foreach (var id in ids) await dispatcher.TryDispatchAsync(id, ct);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        // Each actual factory owns and stops its native server/hosted listener.
        var failures = new List<Exception>();
        if (Gateway is not null) await StopFactoryAsync("gateway", Gateway, ct, failures);
        if (Api is not null) await StopFactoryAsync("api", Api, ct, failures);
        if (Notifier is not null) await StopFactoryAsync("notifier", Notifier, ct, failures);
        if (failures.Count > 0) throw new AggregateException("Actual server shutdown failed.", failures);
    }

    private async Task StopFactoryAsync<T>(string label, WebApplicationFactory<T> factory, CancellationToken ct,
        List<Exception> failures) where T : class
    {
        try
        {
            // Native WAF10.0.12 disposes its deferred IHost via StopAsync. Stop
            // its actual Kestrel listener explicitly first, then retain the
            // factory's normal hosted-service/disposal path.
            await factory.Services.GetRequiredService<IServer>().StopAsync(ct);
        }
        catch (Exception error) { Record("listener", error); }
        try { await factory.DisposeAsync(); }
        catch (Exception error) { Record("factory", error); }
        void Record(string step, Exception error)
        {
            failures.Add(error);
            StopFailures.Add(new { host = label, step, errorType = error.GetType().Name, innerType = error.InnerException?.GetType().Name });
        }
    }

    private void ConfigureClock(IServiceCollection services)
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(clock);
        services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
        services.Configure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
    }

    private void RemoveOwnedWorkers(IServiceCollection services, bool notifier)
    {
        foreach (var descriptor in services.Where(value => value.ServiceType == typeof(IHostedService)
            && (value.ImplementationType?.Name is "BootstrapAdminHostedService" or "TaskReminderScheduler"
                or "TaskNotificationScheduler" or "AiDraftCleanupWorker" or "NotificationRetentionWorker"
                || notifier && value.ImplementationFactory is not null
                || settings.Transport == "Controlled" && value.ImplementationType == typeof(NotifierListener))).ToArray())
            services.Remove(descriptor);
        // Rabbit mode retains the actual RabbitMqListenerService registration.
    }

    private Dictionary<string, string?> Configuration() => new()
    {
        ["ConnectionStrings:DbConnection"] = database.ConnectionString,
        ["Database:Authentication"] = "ConnectionString",
        ["Jwt:Key"] = "full-stack-synthetic-" + settings.RunId.ToString("N"),
        ["Jwt:Issuer"] = "TaskManagerAPI", ["Jwt:Audience"] = "TaskManagerClients",
        ["BrowserAuth:PublicOrigin"] = publicOrigin.GetLeftPart(UriPartial.Authority),
        ["BrowserAuth:SessionMinutes"] = "60", ["BrowserAuth:AuthRequestsPerMinute"] = "1000",
        ["Email:Provider"] = "Smtp", ["Email:SmtpHost"] = "127.0.0.1", ["Email:SmtpPort"] = "1025",
        ["Email:UseTls"] = "false", ["Email:FromAddress"] = "noreply@taskmanager.example.test",
        ["DataProtection:Provider"] = "FileSystem", ["DataProtection:KeyRingPath"] = Path.Combine(settings.WorkingDirectory, "keyring"),
        ["Telemetry:Provider"] = "Disabled", ["BootstrapAdmin:Enabled"] = "false", ["Reminders:Enabled"] = "false",
        ["Notifications:Provider"] = "RabbitMQ",
        ["RabbitMQ:HostName"] = settings.RabbitHost, ["RabbitMQ:Port"] = settings.RabbitPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["RabbitMQ:UserName"] = settings.RabbitUser, ["RabbitMQ:Password"] = settings.RabbitPassword,
        ["RabbitMQ:QueueName"] = settings.TaskQueue, ["RabbitMQ:ReminderQueueName"] = settings.ReminderQueue,
        ["RabbitMQ:RetryAttempts"] = "3", ["RabbitMQ:RetryDelayMilliseconds"] = "100",
        ["AI:Provider"] = "Fake", ["AI:RequestsPerMinute"] = "30",
        ["Gateway:ApiDestination"] = apiOrigin.AbsoluteUri, ["Gateway:NotifierDestination"] = notifierOrigin.AbsoluteUri,
        ["ProxyTrust:KnownProxies:0"] = "127.0.0.1", ["ProxyTrust:AllowedHosts:0"] = "127.0.0.1", ["ProxyTrust:ForwardLimit"] = "1",
        ["Logging:LogLevel:Default"] = "None", ["Serilog:MinimumLevel:Default"] = "Fatal",
        ["Serilog:MinimumLevel:Override:Microsoft"] = "Fatal", ["Serilog:MinimumLevel:Override:System"] = "Fatal",
        ["AllowedHosts"] = "127.0.0.1"
    };

    private sealed class ActualFactory<T>(string contentRoot, Dictionary<string, string?> configuration,
        Action<IServiceCollection> customize, string? webRoot = null) : WebApplicationFactory<T> where T : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing").UseContentRoot(contentRoot);
            if (webRoot is not null) builder.UseWebRoot(webRoot);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
            builder.ConfigureLogging(logging => logging.ClearProviders().SetMinimumLevel(LogLevel.None));
            builder.ConfigureServices(services =>
            {
                customize(services);
                services.RemoveAll<ILoggerFactory>();
                services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            });
        }
    }
}
