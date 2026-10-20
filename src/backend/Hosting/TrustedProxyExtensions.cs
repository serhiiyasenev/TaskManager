using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TaskManager.Hosting;

public static class TrustedProxyExtensions
{
    public static IApplicationBuilder UseTrustedProxyForwarding(this IApplicationBuilder app) =>
        // The framework permits null peers for some servers; no address means no trusted hop here.
        app.UseWhen(context => context.Connection.RemoteIpAddress is not null, branch => branch.UseForwardedHeaders());

    public static IServiceCollection AddTrustedProxyForwarding(this IServiceCollection services, IConfiguration configuration)
    {
        // Reject unsafe values already present, then read the finalized providers when options resolve.
        _ = ReadTrust(configuration);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            var trust = ReadTrust(configuration);
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = trust.ForwardLimit;
            // ACA appends XFF but overwrites XFP. Only known peers may supply
            // these asymmetric lists; the configured one/two-hop limit applies.
            options.RequireHeaderSymmetry = false;
            // Preserve framework loopback defaults. Empty trust collections accept every peer.
            foreach (var proxy in trust.Proxies) options.KnownProxies.Add(proxy);
            foreach (var network in trust.Networks) options.KnownIPNetworks.Add(network);
            foreach (var host in trust.AllowedHosts) options.AllowedHosts.Add(host);
        });
        return services;
    }

    private static TrustSettings ReadTrust(IConfiguration configuration)
    {
        var proxies = configuration.GetSection("ProxyTrust:KnownProxies").GetChildren()
            .Select(entry => ParseProxy(entry.Value)).ToArray();
        var networks = configuration.GetSection("ProxyTrust:KnownNetworks").GetChildren()
            .Select(entry => ParseNetwork(entry.Value)).ToArray();
        var allowedHosts = configuration.GetSection("ProxyTrust:AllowedHosts").GetChildren()
            .Select(entry => ParseHost(entry.Value)).ToArray();
        if (allowedHosts.Length == 0)
        {
            if (!Uri.TryCreate(configuration["BrowserAuth:PublicOrigin"] ?? "https://localhost", UriKind.Absolute, out var origin) ||
                origin.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(origin.UserInfo) ||
                !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != "/")
                throw new InvalidOperationException("ProxyTrust requires a valid BrowserAuth:PublicOrigin.");
            allowedHosts = [origin.HostNameType == UriHostNameType.IPv6 ? $"[{origin.IdnHost.Trim('[', ']')}]" : origin.IdnHost];
        }
        var configuredLimit = configuration["ProxyTrust:ForwardLimit"];
        var forwardLimit = 1;
        if (configuredLimit is not null && (!int.TryParse(configuredLimit,
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
            out forwardLimit) || forwardLimit is < 1 or > 2))
            throw new InvalidOperationException("ProxyTrust:ForwardLimit must be one or two explicitly trusted hops.");
        return new TrustSettings(proxies, networks, allowedHosts, forwardLimit);
    }

    private static IPAddress ParseProxy(string? value)
    {
        if (!IPAddress.TryParse(value, out var address) ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            (address.IsIPv4MappedToIPv6 && address.MapToIPv4().Equals(IPAddress.Any)))
            throw new InvalidOperationException("ProxyTrust:KnownProxies must contain explicit IP addresses.");
        return address;
    }

    private static System.Net.IPNetwork ParseNetwork(string? value)
    {
        if (!System.Net.IPNetwork.TryParse(value, out var network) || network.PrefixLength == 0)
            throw new InvalidOperationException("ProxyTrust:KnownNetworks must contain bounded valid CIDR networks.");
        return network;
    }

    private static string ParseHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('*') ||
            Uri.CheckHostName(value.Trim('[', ']')) == UriHostNameType.Unknown)
            throw new InvalidOperationException("ProxyTrust:AllowedHosts must contain explicit host names without ports or wildcards.");
        return value;
    }

    private sealed record TrustSettings(IPAddress[] Proxies, System.Net.IPNetwork[] Networks, string[] AllowedHosts, int ForwardLimit);
}
