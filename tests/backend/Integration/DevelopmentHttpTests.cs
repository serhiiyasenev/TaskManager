using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TaskManager.Hosting;
using Xunit;

namespace Tests.Integration;

public sealed class DevelopmentHttpTests
{
    [Theory]
    [InlineData("Development", "http://localhost:7150", "localhost:7150", "127.0.0.1", true)]
    [InlineData("Development", "http://localhost:7150", "localhost:7150", "::1", true)]
    [InlineData("Development", "http://localhost:7150", "127.0.0.1:7150", "127.0.0.1", false)]
    [InlineData("Development", "http://localhost:7150", "localhost:7151", "127.0.0.1", false)]
    [InlineData("Development", "http://localhost:7150", "localhost:7150", "192.0.2.1", false)]
    [InlineData("Development", "http://localhost:7150", "localhost:7150", null, false)]
    [InlineData("Production", "http://localhost:7150", "localhost:7150", "127.0.0.1", false)]
    [InlineData("Testing", "http://localhost:7150", "localhost:7150", "127.0.0.1", false)]
    [InlineData("Development", "https://localhost:7151", "localhost:7150", "127.0.0.1", false)]
    public void RedirectException_RequiresDevelopmentExplicitOriginAndLoopbackPeer(
        string environment, string origin, string host, string? remote, bool allowed)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString(host);
        context.Connection.RemoteIpAddress = remote is null ? null : IPAddress.Parse(remote);

        Assert.Equal(allowed, AllowedBrowserOrigins.AllowsDevelopmentHttp(context, Config(origin), new TestEnvironment(environment)));
    }

    [Fact]
    public void ExplicitAdditionalLoopbackOrigin_EnablesHttpOnlyInDevelopment()
    {
        var configuration = Config("https://localhost:7151");
        Assert.False(AllowedBrowserOrigins.HasDevelopmentHttp(configuration, new TestEnvironment("Development")));
        configuration["BrowserAuth:AdditionalAllowedOrigins:0"] = "http://localhost:7150";
        Assert.True(AllowedBrowserOrigins.HasDevelopmentHttp(configuration, new TestEnvironment("Development")));
        Assert.False(AllowedBrowserOrigins.HasDevelopmentHttp(configuration, new TestEnvironment("Testing")));
        Assert.False(AllowedBrowserOrigins.HasDevelopmentHttp(configuration, new TestEnvironment("Production")));
    }

    [Theory]
    [InlineData("Development", "http://localhost:7150", true)]
    [InlineData("Development", "https://localhost:7151", false)]
    [InlineData("Testing", "https://localhost:7151", false)]
    [InlineData("Testing", "http://localhost:7150", false)]
    public async Task CookieOptions_UseDistinctLocalNamesOnlyForExplicitDevelopmentHttp(string environment, string origin, bool local)
    {
        await using var factory = new BrowserAuthenticationFactory(environmentName: environment, publicOrigin: origin);
        var session = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var csrf = factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        var policy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        Assert.Equal(local ? "TaskManager.Local.Session" : "__Host-TaskManager.Session", session.Cookie.Name);
        Assert.Equal(local ? "TaskManager.Local.Antiforgery" : "__Host-TaskManager.Antiforgery", csrf.Cookie.Name);
        Assert.Equal(policy, session.Cookie.SecurePolicy);
        Assert.Equal(policy, csrf.Cookie.SecurePolicy);
        Assert.True(session.Cookie.HttpOnly);
        Assert.True(csrf.Cookie.HttpOnly);
        Assert.Equal("/", session.Cookie.Path);
        Assert.Equal("/", csrf.Cookie.Path);
    }

    private static IConfigurationRoot Config(string origin) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["BrowserAuth:PublicOrigin"] = origin }).Build();

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "DevelopmentHttpTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
