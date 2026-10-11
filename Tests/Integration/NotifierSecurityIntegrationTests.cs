extern alias NotifierLib;
using System.Net;
using System.Net.Http.Headers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NotifierLib::Notifier;
using Xunit;

namespace Tests.Integration;

public class NotifierSecurityIntegrationTests : IClassFixture<NotifierSecurityApplicationFactory>
{
    private readonly NotifierSecurityApplicationFactory _factory;
    public NotifierSecurityIntegrationTests(NotifierSecurityApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/chathub/negotiate?negotiateVersion=1", "POST")]
    [InlineData("/chathub?id=untrusted-connection", "GET")]
    public async Task AnonymousHubRequests_Return401WithoutRedirect(string path, string method)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task InvalidOrExpiredBearer_CannotNegotiate(bool expired, bool invalidSignature)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.CreateToken(expired, invalidSignature));
        using var response = await client.PostAsync("/chathub/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidBearer_CanNegotiate()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.CreateToken());
        using var response = await client.PostAsync("/chathub/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task QueryStringToken_DoesNotAuthorizeHub()
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsync("/chathub/negotiate?negotiateVersion=1&access_token=" + _factory.CreateToken(), null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed class NotifierSecurityApplicationFactory : WebApplicationFactory<NotifierLib::Notifier.Program>
{
    // Synthetic signing configuration belongs only to this isolated test host.
    private const string SigningKey = "NotifierSecuritySyntheticTestConfiguration";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = SigningKey,
            ["Jwt:Issuer"] = "TaskManager",
            ["Jwt:Audience"] = "TaskManagerClients",
            ["ConnectionStrings:DbConnection"] = "Server=localhost;Database=NotifierTests;Integrated Security=True;TrustServerCertificate=True"
        }));
        builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                     d.ImplementationType == typeof(RabbitMqListenerService)).ToList()) services.Remove(descriptor);
        });
    }

    public string CreateToken(bool expired = false, bool invalidSignature = false)
    {
        var signingKey = SHA256.HashData(Encoding.UTF8.GetBytes(invalidSignature ? SigningKey + "invalid" : SigningKey));
        var token = new JwtSecurityToken("TaskManager", "TaskManagerClients",
            [new Claim(JwtRegisteredClaimNames.Sub, "7")],
            DateTime.UtcNow.AddHours(-2), expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(new SymmetricSecurityKey(signingKey), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
