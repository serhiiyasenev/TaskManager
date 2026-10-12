using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace TaskManager.FullStackHost;

internal static class Program
{
    public static async Task<int> Main()
    {
        var errorOutput = Console.Error;
        // Native apps and SDK diagnostics share the child process console. No
        // private URL, SQL exception, cookie, mail or fixture notes enter stdout.
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        FullStackSettings? settings = null;
        FullStackDatabase? database = null;
        FullStackApplications? applications = null;
        WebApplication? control = null;
        OwnedLoopbackCertificate? certificate = null;
        var ports = new List<ReservedPort>();
        var exitCode = 0;
        var phase = "configuration";
        var probes = new List<SafeProbe>();
        try
        {
            settings = FullStackSettings.Read();
            PrivateFiles.CreateDirectory(settings.RunDirectory);
            PrivateFiles.CreateDirectory(settings.WorkingDirectory);
            PrivateFiles.CreateDirectory(Path.Combine(settings.WorkingDirectory, "keyring"));
            database = new FullStackDatabase(settings);
            using var startupDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            phase = "migrations";
            await database.CreateAsync(startupDeadline.Token);
            for (var index = 0; index < 4; index++) ports.Add(new ReservedPort());
            var apiOrigin = new Uri($"http://127.0.0.1:{ports[0].Port}/");
            var notifierOrigin = new Uri($"http://127.0.0.1:{ports[1].Port}/");
            var publicOrigin = new Uri($"https://127.0.0.1:{ports[2].Port}/");
            var controlOrigin = new Uri($"http://127.0.0.1:{ports[3].Port}/");
            var clock = new FullStackClock();
            var mail = new FullStackMail(publicOrigin);
            var aiMode = new FullStackAiMode();
            var evidence = new SettlementEvidence(database);
            var controlled = new ControlledQueue(settings, evidence);
            applications = new FullStackApplications(settings, database, clock, mail, aiMode, controlled,
                evidence, publicOrigin, apiOrigin, notifierOrigin);
            applications.SetEarlyProcessConfiguration();
            phase = "certificate";
            certificate = new OwnedLoopbackCertificate(settings.RunId);
            phase = "api-start";
            applications.StartApi(ports[0]);
            phase = "notifier-start";
            applications.StartNotifier(ports[1]);
            phase = "gateway-start";
            applications.StartGateway(ports[2], certificate.Certificate);
            phase = "native-health";
            await VerifyActualHostsAsync(apiOrigin, notifierOrigin, publicOrigin, certificate.Certificate, probes, startupDeadline.Token);
            var controlToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            control = CreateControl(ports[3].Port, controlToken, database, applications, clock, mail, aiMode, evidence);
            ports[3].Dispose();
            control.Lifetime.ApplicationStopping.Register(() => stopped.TrySetResult());
            await control.StartAsync(startupDeadline.Token);
            phase = "state";
            await PrivateFiles.WriteNewJsonAsync(settings.StatePath, new
            {
                schemaVersion = 1, mode = "controlled-full-stack", baseUrl = publicOrigin.GetLeftPart(UriPartial.Authority),
                controlUrl = controlOrigin.GetLeftPart(UriPartial.Authority), controlToken,
                databaseName = settings.DatabaseName, provider = "fake",
                transport = settings.Transport == "Controlled" ? "controlled" : "rabbitmq"
            }, startupDeadline.Token);
            phase = "running";
            Console.CancelKeyPress += (_, args) => { args.Cancel = true; stopped.TrySetResult(); };
            // The runner holds redirected stdin open and closes it to request a
            // portable graceful stop. No EOF can be mistaken for a cleanup PASS.
            _ = Task.Run(() => WatchStdinAsync(stopped));
            await stopped.Task;
        }
        catch (Exception error)
        {
            exitCode = 1;
            // Diagnostic categories are bounded. Never serialize Message/StackTrace.
            if (settings is not null)
                try { await PrivateFiles.WriteNewJsonAsync(Path.Combine(settings.RunDirectory, "failure.json"),
                    new { schemaVersion = 1, mode = "controlled-full-stack-failure", phase, errorType = error.GetType().Name,
                        probes, bindings = applications?.GetBindingDiagnostics() }); }
                catch (Exception) { }
            await errorOutput.WriteLineAsync("Full-stack verification host failed; inspect the private bounded failure category.");
        }
        finally
        {
            var cleanupFailed = false;
            var cleanupErrors = new List<SafeCleanupFailure>();
            using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            if (control is not null)
            {
                try { await control.StopAsync(cleanupDeadline.Token); await control.DisposeAsync(); }
                catch (Exception error) { cleanupFailed = true; cleanupErrors.Add(new("control-stop", ErrorTypes(error))); }
            }
            if (applications is not null)
                try { await applications.StopAsync(cleanupDeadline.Token); }
                catch (Exception error) { cleanupFailed = true; cleanupErrors.Add(new("application-stop", ErrorTypes(error))); }
            foreach (var port in ports) port.Dispose();
            var serversStopped = true;
            var closedListeners = new List<SafeClosedListener>();
            var labels = new[] { "api", "notifier", "gateway", "control" };
            for (var index = 0; index < ports.Count; index++)
            {
                var listener = await ProbeClosedAsync(labels[index], ports[index].Port);
                closedListeners.Add(listener);
                if (!listener.Closed) serversStopped = false;
            }
            if (certificate is not null)
                try { certificate.Dispose(); }
                catch (Exception error) { cleanupFailed = true; cleanupErrors.Add(new("certificate-key-delete", ErrorTypes(error))); }
            var queuesDeleted = settings?.Transport == "Controlled";
            if (settings is not null)
                try { await OwnedRabbitQueues.DeleteAsync(settings, cleanupDeadline.Token); queuesDeleted = true; }
                catch (Exception error) { cleanupFailed = true; cleanupErrors.Add(new("broker-delete", ErrorTypes(error))); }
            var databaseDropped = false;
            if (database is not null)
                try { await database.DropAsync(cleanupDeadline.Token); databaseDropped = true; }
                catch (Exception error) { cleanupFailed = true; cleanupErrors.Add(new("database-drop", ErrorTypes(error))); }
            if (settings is not null)
            {
                try
                {
                    if (Directory.Exists(settings.WorkingDirectory)) Directory.Delete(settings.WorkingDirectory, recursive: true);
                    if (!cleanupFailed && serversStopped && queuesDeleted && databaseDropped)
                        await PrivateFiles.WriteNewJsonAsync(settings.CleanupPath, new
                        {
                            schemaVersion = 1, mode = "controlled-full-stack-cleanup", databaseName = settings.DatabaseName,
                            databaseDropped, serversStopped, brokerQueuesDeleted = queuesDeleted
                        }, cleanupDeadline.Token);
                    else cleanupFailed = true;
                }
                catch (Exception error) { cleanupFailed = true; cleanupErrors.Add(new("private-files-or-proof", ErrorTypes(error))); }
                if (cleanupFailed)
                    try { await PrivateFiles.WriteNewJsonAsync(Path.Combine(settings.RunDirectory, "cleanup-failure.json"),
                        new { schemaVersion = 1, mode = "controlled-full-stack-cleanup-failure", databaseName = settings.DatabaseName,
                            databaseDropped, serversStopped, brokerQueuesDeleted = queuesDeleted, steps = cleanupErrors,
                            applicationStops = applications?.StopFailures, closedListeners }); }
                    catch (Exception) { }
            }
            if (cleanupFailed) { exitCode = 1; await errorOutput.WriteLineAsync("Full-stack cleanup could not be verified."); }
        }
        return exitCode;
    }

    private static WebApplication CreateControl(int port, string secret, FullStackDatabase database,
        FullStackApplications applications, FullStackClock clock, FullStackMail mail, FullStackAiMode mode,
        SettlementEvidence evidence)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
            options.Limits.MaxRequestBodySize = 4096;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            options.Limits.MaxConcurrentConnections = 16;
        });
        var app = builder.Build();
        var secretBytes = Encoding.ASCII.GetBytes(secret);
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            var token = context.Request.Headers["X-FullStack-Control"];
            if (context.Connection.RemoteIpAddress is null || !IPAddress.IsLoopback(context.Connection.RemoteIpAddress)
                || token.Count != 1 || token[0]?.Length != secret.Length
                || !CryptographicOperations.FixedTimeEquals(secretBytes, Encoding.ASCII.GetBytes(token[0]!)))
            { context.Response.StatusCode = 401; return; }
            await next(context);
        });
        app.MapGet("/mail", (string email) =>
        {
            if (email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email)
                return Results.BadRequest(new { code = "invalid_email" });
            return Results.Ok(mail.Get(email));
        });
        app.MapPost("/clock/advance", (AdvanceClock request) =>
        {
            try { return Results.Ok(new { nowUtc = clock.Advance(request.Minutes) }); }
            catch (ArgumentOutOfRangeException) { return Results.BadRequest(new { code = "invalid_clock_advance" }); }
        });
        app.MapPost("/ai/mode", (ChangeAiMode request) =>
        {
            try { mode.Set(request.Mode); return Results.Ok(new { mode = mode.Value }); }
            catch (ArgumentException) { return Results.BadRequest(new { code = "invalid_ai_mode" }); }
        });
        var dispatchLock = new SemaphoreSlim(1, 1);
        app.MapPost("/dispatch", async (CancellationToken ct) =>
        {
            if (!await dispatchLock.WaitAsync(0, ct)) return Results.Conflict(new { code = "dispatch_busy" });
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(60));
                await applications.DispatchAsync(deadline.Token);
                return Results.Ok(new { completed = true });
            }
            catch (Exception) when (!ct.IsCancellationRequested) { return Results.Problem(statusCode: 503, title: "dispatch_unavailable"); }
            finally { dispatchLock.Release(); }
        });
        app.MapGet("/summary", async (CancellationToken ct) => Results.Ok(await database.GetSummaryAsync(evidence.Count, ct)));
        return app;
    }

    private static async Task WatchStdinAsync(TaskCompletionSource stopped)
    {
        try
        {
            var buffer = new char[128];
            while (await Console.In.ReadAsync(buffer) > 0) { }
        }
        finally { stopped.TrySetResult(); }
    }

    private static async Task VerifyActualHostsAsync(Uri api, Uri notifier, Uri gateway, X509Certificate2 certificate,
        List<SafeProbe> probes, CancellationToken ct)
    {
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
            ServerCertificateCustomValidationCallback = (_, peer, _, _) => peer is not null && peer.RawData.AsSpan().SequenceEqual(certificate.RawData)
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        foreach (var (label, origin) in new[] { ("api", api), ("notifier", notifier), ("gateway", gateway) })
        {
            var healthy = false;
            int? status = null;
            string? failure = null;
            for (var attempt = 0; attempt < 30 && !healthy; attempt++)
            {
                try
                {
                    using var response = await client.GetAsync(new Uri(origin, "/health/ready"), ct);
                    status = (int)response.StatusCode;
                    failure = null;
                    healthy = response.IsSuccessStatusCode;
                }
                catch (HttpRequestException error) when (!ct.IsCancellationRequested) { failure = error.HttpRequestError.ToString(); }
                catch (Exception error) when (!ct.IsCancellationRequested) { failure = error.GetType().Name; }
                if (!healthy) await Task.Delay(500, ct);
            }
            probes.Add(new(label, "ready", status, failure));
        }
        if (probes.Any(value => value.Status is not (>= 200 and < 300)))
            throw new InvalidOperationException("An actual full-stack readiness check failed.");
        using var index = await client.GetAsync(gateway, ct);
        probes.Add(new("gateway", "index", (int)index.StatusCode, null));
        if (!index.IsSuccessStatusCode || index.Content.Headers.ContentType?.MediaType != "text/html")
            throw new InvalidOperationException("The actual frontend is unavailable through the HTTPS gateway.");
    }

    private static async Task<SafeClosedListener> ProbeClosedAsync(string endpoint, int port)
    {
        bool? present = null;
        string? tableError = null;
        try
        {
            present = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(value => value.Port == port
                && (IPAddress.IsLoopback(value.Address) || value.Address.Equals(IPAddress.Any) || value.Address.Equals(IPAddress.IPv6Any)));
        }
        catch (Exception error) { tableError = error.GetType().Name; }
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connection = "connected";
        try { await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token); }
        catch (SocketException error) { connection = error.SocketErrorCode.ToString(); }
        catch (OperationCanceledException) { connection = "timeout"; }
        // Kernel listener absence is independent of Windows SYN/retry timing.
        // An actually accepted connection always fails cleanup verification.
        var closed = present == false && connection != "connected";
        return new(endpoint, port, present, connection, tableError, closed);
    }

    private sealed record AdvanceClock(int Minutes);
    private sealed record ChangeAiMode(string Mode);
    private sealed record SafeProbe(string Endpoint, string Check, int? Status, string? Failure);
    private sealed record SafeCleanupFailure(string Step, IReadOnlyList<string> ErrorTypes);
    private sealed record SafeClosedListener(string Endpoint, int Port, bool? OsListenerPresent,
        string Connection, string? TableError, bool Closed);
    private static IReadOnlyList<string> ErrorTypes(Exception error)
    {
        var types = new List<string>();
        if (error is AggregateException aggregate)
            return aggregate.Flatten().InnerExceptions.Take(8).Select(value => value.GetType().Name).ToArray();
        for (Exception? current = error; current is not null && types.Count < 8; current = current.InnerException)
            types.Add(current.GetType().Name);
        return types;
    }
}
