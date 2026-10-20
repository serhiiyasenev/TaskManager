using System.Net;
using System.Net.Http.Json;
using Client.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tests.Unit;

public class ConsoleSessionTests
{
    [Fact]
    public async Task Login_UsesExistingJwtEndpointAndKeepsTokenOnlyInSessionAndHeader()
    {
        var handler = new LoginHandler(HttpStatusCode.OK, new { id = 7, token = "synthetic-test-token" });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        var session = new ConsoleSession(http);
        Assert.True(await session.LoginAsync("member@example.test", "synthetic password"));
        Assert.Equal("/api/Users/Login", handler.Path);
        Assert.Equal(7, session.UserId);
        Assert.Equal("synthetic-test-token", session.AccessToken);
        Assert.Equal("Bearer", http.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal("synthetic-test-token", http.DefaultRequestHeaders.Authorization?.Parameter);
    }

    [Fact]
    public async Task FailedLogin_RemainsAnonymousAndDoesNotRetainCredentials()
    {
        using var http = new HttpClient(new LoginHandler(HttpStatusCode.Unauthorized, new { error = "Invalid credentials" }))
            { BaseAddress = new Uri("https://example.test/api/") };
        var session = new ConsoleSession(http);
        Assert.False(await session.LoginAsync("member@example.test", "wrong password"));
        Assert.Null(session.AccessToken);
        Assert.Null(session.UserId);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task Logout_ClearsTokenAndApiAuthorizationHeader()
    {
        using var http = new HttpClient(new LoginHandler(HttpStatusCode.OK, new { id = 7, token = "synthetic-test-token" }))
            { BaseAddress = new Uri("https://example.test/api/") };
        var session = new ConsoleSession(http);
        await session.LoginAsync("member@example.test", "synthetic password");
        session.Logout();
        Assert.Null(session.AccessToken);
        Assert.Null(session.UserId);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void ConnectionOptions_UseConfiguredApiAndActualNotifierHub()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:BaseUrl"] = "http://webapi:8080/api/",
            ["SignalR:HubUrl"] = "http://notifier:8080/chathub"
        }).Build();
        var options = ClientConnectionOptions.FromConfiguration(config);
        Assert.Equal("http://webapi:8080/api/", options.ApiBaseUrl.AbsoluteUri);
        Assert.Equal("http://notifier:8080/chathub", options.HubUrl.AbsoluteUri);
    }

    private sealed class LoginHandler(HttpStatusCode status, object body) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(body) });
        }
    }
}
