using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using TaskManager.Hosting;

namespace TaskManager.WebHost;

internal sealed record GatewayConfiguration(Uri PublicOrigin, Uri ApiDestination, IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters)
{
    public IReadOnlyList<Uri> AllowedOrigins { get; init; } = [];
    public static GatewayConfiguration Read(IConfiguration configuration, IHostEnvironment environment)
    {
        var api = ReadDestination(configuration, environment, "Gateway:ApiDestination");
        var notifier = ReadDestination(configuration, environment, "Gateway:NotifierDestination");
        var development = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        if (!Uri.TryCreate(configuration["BrowserAuth:PublicOrigin"], UriKind.Absolute, out var origin) ||
            !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != "/" ||
            (origin.Scheme != Uri.UriSchemeHttps && !(development && origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback)))
            throw new InvalidOperationException("Gateway: BrowserAuth:PublicOrigin must be an explicit HTTPS origin.");

        return new GatewayConfiguration(origin, api,
            [Route("api", "/api/{**remainder}"), Route("notifier", "/chathub/{**remainder}")],
            [Cluster("api", api), Cluster("notifier", notifier)])
            { AllowedOrigins = AllowedBrowserOrigins.Read(configuration, development) };
    }

    private static Uri ReadDestination(IConfiguration configuration, IHostEnvironment environment, string key)
    {
        var development = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        if (!Uri.TryCreate(configuration[key], UriKind.Absolute, out var address) ||
            !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) ||
            !string.IsNullOrEmpty(address.Fragment) || address.AbsolutePath != "/" ||
            (address.Scheme != Uri.UriSchemeHttps && !(development && address.Scheme == Uri.UriSchemeHttp)))
            throw new InvalidOperationException($"{key} must be an absolute HTTPS root URL without credentials, query or fragment (HTTP is allowed only in development/testing).");
        return address;
    }

    private static RouteConfig Route(string id, string path) => new()
    {
        RouteId = id, ClusterId = id, Match = new RouteMatch { Path = path },
        Transforms =
        [
            new Dictionary<string, string> { ["X-Forwarded"] = "Set", ["Prefix"] = "Remove" },
            new Dictionary<string, string> { ["RequestHeaderRemove"] = "Forwarded" },
            new Dictionary<string, string> { ["RequestHeaderRemove"] = "X-Original-For" },
            new Dictionary<string, string> { ["RequestHeaderRemove"] = "X-Original-Host" },
            new Dictionary<string, string> { ["RequestHeaderRemove"] = "X-Original-Proto" }
        ]
    };

    private static ClusterConfig Cluster(string id, Uri address) => new()
    {
        ClusterId = id,
        Destinations = new Dictionary<string, DestinationConfig> { [id] = new() { Address = address.AbsoluteUri } },
        HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(3) }
    };
}
