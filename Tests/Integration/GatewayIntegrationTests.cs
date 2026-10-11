using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using GatewayProgram = TaskManager.WebHost.Program;
using Task = System.Threading.Tasks.Task;

namespace Tests.Integration;

public class GatewayIntegrationTests : IClassFixture<GatewayFixture>
{
    private readonly GatewayFixture _fixture;
    public GatewayIntegrationTests(GatewayFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ApiRequest_PreservesMethodPathQueryBodyAndBrowserCredentials()
    {
        using var client = _fixture.Factory.CreateClient(GatewayFixture.ClientOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tasks?state=InProgress&page=2")
        {
            Content = new StringContent("{\"name\":\"Завдання\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Cookie", "__Host-TaskManager.Session=synthetic-session");
        request.Headers.Add("Authorization", "Bearer synthetic-native-token");
        request.Headers.Add("Origin", "https://localhost:7151");
        request.Headers.Add("X-CSRF-TOKEN", "synthetic-csrf-proof");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var observed = await response.Content.ReadFromJsonAsync<UpstreamRequest>();
        Assert.Equal("api", observed!.Upstream);
        Assert.Equal("POST", observed.Method);
        Assert.Equal("/api/tasks", observed.Path);
        Assert.Equal("?state=InProgress&page=2", observed.Query);
        Assert.Contains("Завдання", observed.Body);
        Assert.Equal("__Host-TaskManager.Session=synthetic-session", observed.Cookie);
        Assert.Equal("Bearer synthetic-native-token", observed.Authorization);
        Assert.Equal("https://localhost:7151", observed.Origin);
        Assert.Equal("synthetic-csrf-proof", observed.Csrf);
    }

    [Fact]
    public async Task ClientForwardedHeaders_AreReplacedWithActualGatewayOrigin()
    {
        using var client = _fixture.Factory.CreateClient(GatewayFixture.ClientOptions);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/inspect");
        request.Headers.Add("X-Forwarded-Proto", "http");
        request.Headers.Add("X-Forwarded-Host", "attacker.example.test");
        request.Headers.Add("X-Forwarded-For", "198.51.100.88");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var observed = await response.Content.ReadFromJsonAsync<UpstreamRequest>();
        Assert.Equal("https", observed!.ForwardedProto);
        Assert.Equal("localhost:7151", observed.ForwardedHost);
        Assert.DoesNotContain("198.51.100.88", observed.ForwardedFor);
    }

    [Fact]
    public async Task ApiCookie_IsReturnedWithoutChangingHostCookieContract()
    {
        using var client = _fixture.Factory.CreateClient(GatewayFixture.ClientOptions);
        using var response = await client.PostAsync("/api/auth/login", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-TaskManager.Session=synthetic-session", cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/api/missing", HttpStatusCode.NotFound)]
    [InlineData("/api/unauthorized", HttpStatusCode.Unauthorized)]
    public async Task UpstreamFailure_PreservesStatusAndDoesNotBecomeSpaHtml(string path, HttpStatusCode expected)
    {
        using var client = _fixture.Factory.CreateClient(GatewayFixture.ClientOptions);
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("gateway-test-shell", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignalRNegotiation_UsesNotifierClusterAndPreservesCookieAndOrigin()
    {
        using var client = _fixture.Factory.CreateClient(GatewayFixture.ClientOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/chathub/negotiate?negotiateVersion=1");
        request.Headers.Add("Cookie", "__Host-TaskManager.Session=synthetic-session");
        request.Headers.Add("Origin", "https://localhost:7151");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var observed = await response.Content.ReadFromJsonAsync<UpstreamRequest>();
        Assert.Equal("notifier", observed!.Upstream);
        Assert.Equal("/chathub/negotiate", observed.Path);
        Assert.Equal("?negotiateVersion=1", observed.Query);
        Assert.Equal("__Host-TaskManager.Session=synthetic-session", observed.Cookie);
        Assert.Equal("https://localhost:7151", observed.Origin);
    }

    [Fact]
    public async Task WebSocket_UpgradesThroughGatewayAndRelaysFramesWithBrowserCookie()
    {
        // TestServer has no IHttpUpgradeFeature; real Kestrel is required for YARP upgrades.
        using var key = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(names.Build());
        using var generatedCertificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        // A PKCS#12 roundtrip makes the private key usable by Windows Schannel as well as OpenSSL.
        using var certificate = X509CertificateLoader.LoadPkcs12(generatedCertificate.Export(X509ContentType.Pfx), null);
        using var factory = _fixture.NewFactory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps(certificate)));
        factory.StartServer();
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Assert.StartsWith("https://", address, StringComparison.OrdinalIgnoreCase);
        var socketAddress = new UriBuilder(address) { Scheme = "wss", Host = "localhost", Path = "/chathub" }.Uri;
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Cookie", "__Host-TaskManager.Session=synthetic-session");
        socket.Options.SetRequestHeader("Origin", "https://localhost:7151");
        socket.Options.RemoteCertificateValidationCallback = (_, peerCertificate, _, _) =>
            peerCertificate is not null && peerCertificate.GetCertHashString() == certificate.Thumbprint;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.ConnectAsync(socketAddress, timeout.Token);
        var payload = Encoding.UTF8.GetBytes("synthetic-private-frame");
        await socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[128];
        var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Assert.Equal(WebSocketMessageType.Text, received.MessageType);
        Assert.Equal("synthetic-private-frame", Encoding.UTF8.GetString(buffer, 0, received.Count));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "complete", timeout.Token);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/projects/42/board")]
    [InlineData("/account/confirm-email")]
    public async Task BrowserRoute_ServesBuiltSpaShell(string path)
    {
        using var client = _fixture.Factory.CreateClient(GatewayFixture.ClientOptions);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("gateway-test-shell", await response.Content.ReadAsStringAsync());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Theory]
    [InlineData("/auth/unknown")]
    [InlineData("/health/unknown")]
    [InlineData("/assets/missing.js")]
    public async Task ReservedOrMissingAssetRoute_DoesNotFallBackToSpa(string path)
    {
        using var client = _fixture.Factory.CreateClient(GatewayFixture.ClientOptions);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("gateway-test-shell", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StaticAsset_IsServedAsAsset()
    {
        using var client = _fixture.Factory.CreateClient(GatewayFixture.ClientOptions);
        using var response = await client.GetAsync("/assets/app.test.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(".fixture { color: blue; }", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task GatewayProbe_IsIndependentMinimalEndpoint(string path)
    {
        using var client = _fixture.Factory.CreateClient(GatewayFixture.ClientOptions);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("gateway-test-shell", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(503)]
    [InlineData(307)]
    public async Task ApiReadinessFailure_RemovesGatewayFromReadinessButKeepsLiveness(int upstreamStatus)
    {
        _fixture.SetApiReadiness(upstreamStatus);
        try
        {
            using var client = _fixture.Factory.CreateClient(GatewayFixture.ClientOptions);
            var previousProbes = _fixture.ApiReadinessRequests;
            using var live = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal(previousProbes, _fixture.ApiReadinessRequests);
            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            Assert.True(_fixture.ApiReadinessRequests > previousProbes);
            Assert.Null(ready.Headers.Location);
        }
        finally { _fixture.SetApiReadiness(200); }
    }

    [Theory]
    [InlineData("Gateway:ApiDestination", "")]
    [InlineData("Gateway:NotifierDestination", "")]
    [InlineData("Gateway:ApiDestination", "ftp://backend.example.test/")]
    [InlineData("Gateway:ApiDestination", "https://synthetic:password@backend.example.test/")]
    [InlineData("Gateway:ApiDestination", "https://backend.example.test/api/")]
    [InlineData("Gateway:NotifierDestination", "https://backend.example.test/?key=synthetic")]
    [InlineData("Gateway:NotifierDestination", "https://backend.example.test/#fragment")]
    public void UnsafeOrMissingDestination_FailsStartup(string key, string value)
    {
        using var factory = _fixture.NewFactory(new Dictionary<string, string?> { [key] = value });
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient(GatewayFixture.ClientOptions));
        Assert.Contains("Gateway:", error.ToString());
    }

    [Fact]
    public void ProductionPlainHttpDestination_FailsStartup()
    {
        using var factory = _fixture.NewFactory(new Dictionary<string, string?>
        {
            ["Gateway:ApiDestination"] = "http://api.internal.test/",
            ["Gateway:NotifierDestination"] = "https://notifier.internal.test/"
        }, "Production");
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient(GatewayFixture.ClientOptions));
        Assert.Contains("Gateway:ApiDestination", error.ToString());
    }

    [Fact]
    public void GatewayAssembly_HasNoBusinessDatabaseDependency()
    {
        var references = typeof(GatewayProgram).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.DoesNotContain("DAL", references);
        Assert.DoesNotContain("BLL", references);
        Assert.DoesNotContain(references, name => name?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true);
    }

    public sealed record UpstreamRequest(string Upstream, string Method, string Path, string Query, string Body,
        string Cookie, string Authorization, string Origin, string Csrf, string ForwardedFor, string ForwardedProto, string ForwardedHost);
}

public sealed class GatewayFixture : IAsyncLifetime
{
    private WebApplication? _api;
    private WebApplication? _notifier;
    private string _apiAddress = "";
    private string _notifierAddress = "";
    private int _apiReadinessStatus = 200;
    private int _apiReadinessRequests;
    public int ApiReadinessRequests => Volatile.Read(ref _apiReadinessRequests);
    public void SetApiReadiness(int status) => Volatile.Write(ref _apiReadinessStatus, status);
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "taskmanager-gateway-tests-" + Guid.NewGuid().ToString("N"));
    public GatewayApplicationFactory Factory { get; private set; } = null!;
    public static WebApplicationFactoryClientOptions ClientOptions => new()
    {
        BaseAddress = new Uri("https://localhost:7151"), AllowAutoRedirect = false, HandleCookies = false
    };

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), "<!doctype html><html lang=\"uk\"><body>gateway-test-shell</body></html>");
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "assets", "app.test.css"), ".fixture { color: blue; }");
        _api = CreateUpstream();
        _api.MapGet("/health/ready", (HttpContext context) =>
        {
            Interlocked.Increment(ref _apiReadinessRequests);
            context.Response.StatusCode = Volatile.Read(ref _apiReadinessStatus);
            if (context.Response.StatusCode == 307) context.Response.Headers.Location = "/must-not-follow";
            return Task.CompletedTask;
        });
        _api.MapMethods("/api/{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE"], context => Observe(context, "api"));
        await _api.StartAsync();
        _apiAddress = Address(_api);
        _notifier = CreateUpstream();
        _notifier.UseWebSockets();
        _notifier.MapPost("/chathub/negotiate", context => Observe(context, "notifier"));
        _notifier.Map("/chathub", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest ||
                !context.Request.Headers.Cookie.ToString().Contains("synthetic-session", StringComparison.Ordinal))
            {
                context.Response.StatusCode = 401;
                return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var bytes = new byte[256];
            var frame = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), context.RequestAborted);
            await socket.SendAsync(new ArraySegment<byte>(bytes, 0, frame.Count), frame.MessageType, frame.EndOfMessage, context.RequestAborted);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "complete", context.RequestAborted);
        });
        await _notifier.StartAsync();
        _notifierAddress = Address(_notifier);
        Factory = NewFactory();
    }

    public GatewayApplicationFactory NewFactory(Dictionary<string, string?>? overrides = null, string environment = "Testing")
    {
        var settings = new Dictionary<string, string?>
        {
            ["Gateway:ApiDestination"] = _apiAddress,
            ["Gateway:NotifierDestination"] = _notifierAddress,
            ["BrowserAuth:PublicOrigin"] = "https://localhost:7151"
        };
        if (overrides is not null) foreach (var pair in overrides) settings[pair.Key] = pair.Value;
        return new GatewayApplicationFactory(settings, _webRoot, environment);
    }

    public async Task DisposeAsync()
    {
        Factory.Dispose();
        if (_notifier is not null) await _notifier.DisposeAsync();
        if (_api is not null) await _api.DisposeAsync();
        var fullPath = Path.GetFullPath(_webRoot);
        if (fullPath.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(fullPath).StartsWith("taskmanager-gateway-tests-", StringComparison.Ordinal))
            Directory.Delete(fullPath, recursive: true);
    }

    private static WebApplication CreateUpstream()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        return builder.Build();
    }

    private static string Address(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/') + '/';

    private static async Task Observe(HttpContext context, string upstream)
    {
        if (context.Request.Path == "/api/missing" || context.Request.Path == "/api/unauthorized")
        {
            context.Response.StatusCode = context.Request.Path == "/api/missing" ? 404 : 401;
            await context.Response.WriteAsJsonAsync(new { error = "synthetic-upstream-error" });
            return;
        }
        if (context.Request.Path == "/api/auth/login")
            context.Response.Cookies.Append("__Host-TaskManager.Session", "synthetic-session", new CookieOptions
            { Secure = true, HttpOnly = true, Path = "/", SameSite = SameSiteMode.Lax });
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        await context.Response.WriteAsJsonAsync(new GatewayIntegrationTests.UpstreamRequest(upstream,
            context.Request.Method, context.Request.Path.Value!, context.Request.QueryString.Value ?? "", body,
            context.Request.Headers.Cookie.ToString(), context.Request.Headers.Authorization.ToString(),
            context.Request.Headers.Origin.ToString(), context.Request.Headers["X-CSRF-TOKEN"].ToString(),
            context.Request.Headers["X-Forwarded-For"].ToString(), context.Request.Headers["X-Forwarded-Proto"].ToString(),
            context.Request.Headers["X-Forwarded-Host"].ToString()));
    }
}

public sealed class GatewayApplicationFactory(Dictionary<string, string?> settings, string webRoot, string environment)
    : WebApplicationFactory<GatewayProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseWebRoot(webRoot);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
    }
}
