using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskManager.Hosting;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Integration;

public class TrustedForwardingTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.77.24.10")]
    [InlineData("10.77.25.42")]
    public async Task TrustedPeer_RestoresExternalHttpsHostAndClientAddress(string peer)
    {
        using var server = CreateServer(peer);
        using var request = ForwardedRequest();
        using var response = await server.GetTestClient().SendAsync(request);
        var observed = await response.Content.ReadFromJsonAsync<ObservedRequest>();
        Assert.Equal("https", observed!.Scheme);
        Assert.Equal("taskmanager.example.test:7151", observed.Host);
        Assert.Equal("203.0.113.19", observed.ClientAddress);
    }

    [Fact]
    public async Task UnknownPeer_CannotSpoofExternalOriginOrClientAddress()
    {
        using var server = CreateServer("192.0.2.50");
        using var request = ForwardedRequest();
        using var response = await server.GetTestClient().SendAsync(request);
        var observed = await response.Content.ReadFromJsonAsync<ObservedRequest>();
        Assert.Equal("http", observed!.Scheme);
        Assert.Equal("localhost", observed.Host);
        Assert.Equal("192.0.2.50", observed.ClientAddress);
    }

    [Fact]
    public async Task TrustedPeer_CannotReplaceHostWithUnapprovedDomain()
    {
        using var server = CreateServer("10.77.24.10");
        using var request = ForwardedRequest("attacker.example.test");
        using var response = await server.GetTestClient().SendAsync(request);
        var observed = await response.Content.ReadFromJsonAsync<ObservedRequest>();
        Assert.Equal("localhost", observed!.Host);
    }

    [Fact]
    public async Task Forwarding_ConsumesOnlyImmediateHop()
    {
        using var server = CreateServer("10.77.24.10");
        using var request = ForwardedRequest();
        request.Headers.Remove("X-Forwarded-For");
        request.Headers.Add("X-Forwarded-For", "198.51.100.22, 203.0.113.19");
        using var response = await server.GetTestClient().SendAsync(request);
        var observed = await response.Content.ReadFromJsonAsync<ObservedRequest>();
        Assert.Equal("203.0.113.19", observed!.ClientAddress);
    }

    [Fact]
    public async Task LateConfigurationProvider_UpdatesProxyAndPublicHostBeforeStartup()
    {
        using var server = CreateServer("10.77.26.10", configuration =>
        {
            configuration["ProxyTrust:KnownProxies:0"] = "10.77.26.10";
            configuration["BrowserAuth:PublicOrigin"] = "https://late.example.test:7151";
        });
        using var request = ForwardedRequest("late.example.test:7151");
        using var response = await server.GetTestClient().SendAsync(request);
        var observed = await response.Content.ReadFromJsonAsync<ObservedRequest>();
        Assert.Equal("https", observed!.Scheme);
        Assert.Equal("late.example.test:7151", observed.Host);
        Assert.Equal("203.0.113.19", observed.ClientAddress);
    }

    [Theory]
    [InlineData("ProxyTrust:KnownProxies:0", "gateway.local")]
    [InlineData("ProxyTrust:KnownProxies:0", "0.0.0.0")]
    [InlineData("ProxyTrust:KnownNetworks:0", "0.0.0.0/0")]
    [InlineData("ProxyTrust:KnownNetworks:0", "::/0")]
    [InlineData("ProxyTrust:KnownNetworks:0", "10.77.24.0/33")]
    [InlineData("ProxyTrust:AllowedHosts:0", "*")]
    [InlineData("ProxyTrust:AllowedHosts:0", "https://attacker.example.test/")]
    public void InvalidOrUnboundedTrust_FailsDuringRegistration(string key, string value)
    {
        var settings = Settings();
        settings[key] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTrustedProxyForwarding(configuration));
        Assert.Contains("ProxyTrust", error.Message);
    }

    private static IHost CreateServer(string peer, Action<IConfiguration>? configureAfterRegistration = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Settings()).Build();
        return new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services => services.AddTrustedProxyForwarding(configuration))
            .Configure(app =>
            {
                configureAfterRegistration?.Invoke(configuration);
                app.Use((context, next) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                    return next(context);
                });
                app.UseTrustedProxyForwarding();
                app.Run(context => context.Response.WriteAsJsonAsync(new ObservedRequest(
                    context.Request.Scheme, context.Request.Host.ToString(), context.Connection.RemoteIpAddress!.ToString())));
            })).Start();
    }

    private static HttpRequestMessage ForwardedRequest(string host = "taskmanager.example.test:7151")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", host);
        request.Headers.Add("X-Forwarded-For", "203.0.113.19");
        return request;
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["BrowserAuth:PublicOrigin"] = "https://taskmanager.example.test:7151",
        ["ProxyTrust:KnownProxies:0"] = "10.77.24.10",
        ["ProxyTrust:KnownNetworks:0"] = "10.77.25.0/24"
    };

    public sealed record ObservedRequest(string Scheme, string Host, string ClientAddress);
}
