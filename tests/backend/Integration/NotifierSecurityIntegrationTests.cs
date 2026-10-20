extern alias NotifierLib;
using System.Net;
using System.Net.Http.Headers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NotifierLib::Notifier;
using Xunit;
using Task = System.Threading.Tasks.Task;

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

    [Fact]
    public async Task ValidIdentityCookie_CanNegotiate()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", _factory.CreateCookie());
        using var response = await client.PostAsync("/chathub/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MismatchedOrigin_CannotNegotiateWithValidCookie()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", _factory.CreateCookie());
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example.test");
        using var response = await client.PostAsync("/chathub/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ConfiguredOrigin_CanNegotiateWithValidCookie()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", _factory.CreateCookie());
        client.DefaultRequestHeaders.Add("Origin", "https://taskmanager.example.test");
        using var response = await client.PostAsync("/chathub/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RevokedSecurityStamp_CannotNegotiateWithExistingCookie()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", _factory.CreateCookie());
        using (var scope = _factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TaskContext>();
            var user = await database.Users.FindAsync(7000);
            user!.SecurityStamp = "new-synthetic-security-stamp";
            await database.SaveChangesAsync();
        }
        using var response = await client.PostAsync("/chathub/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredIdentityCookie_CannotNegotiate()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", _factory.CreateCookie(expired: true));
        using var response = await client.PostAsync("/chathub/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task InvalidBearer_DoesNotFallBackToValidBrowserCookie()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", _factory.CreateCookie());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.CreateToken(invalidSignature: true));
        using var response = await client.PostAsync("/chathub/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed class NotifierSecurityApplicationFactory : WebApplicationFactory<NotifierLib::Notifier.Program>
{
    // Synthetic signing configuration belongs only to this isolated test host.
    private const string SigningKey = "NotifierSecuritySyntheticTestConfiguration";
    private readonly string _databaseName = Guid.NewGuid().ToString();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = SigningKey,
            ["Jwt:Issuer"] = "TaskManager",
            ["Jwt:Audience"] = "TaskManagerClients",
            ["BrowserAuth:PublicOrigin"] = "https://taskmanager.example.test",
            ["ConnectionStrings:DbConnection"] = "Server=localhost;Database=NotifierTests;Integrated Security=True;TrustServerCertificate=True"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<TaskContext>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.DbContext>();
            services.RemoveAll<DbContextOptions<TaskContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<TaskContext>>();
            services.AddDbContext<TaskContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddScoped<Microsoft.EntityFrameworkCore.DbContext>(provider => provider.GetRequiredService<TaskContext>());
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

    public string CreateCookie(bool expired = false)
    {
        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TaskContext>();
        var user = database.Users.Find(7000);
        if (user is null)
        {
            user = new User("synthetic-notifier-user", "Synthetic", "Member", "synthetic@example.test")
            {
                Id = 7000,
                NormalizedUserName = "SYNTHETIC-NOTIFIER-USER",
                NormalizedEmail = "SYNTHETIC@EXAMPLE.TEST",
                SecurityStamp = "synthetic-security-stamp"
            };
            database.Users.Add(user);
        }
        user.SecurityStamp = "synthetic-security-stamp";
        database.SaveChanges();
        var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7000"),
            new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp)], IdentityConstants.ApplicationScheme);
        var properties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExpiresUtc = expired ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddMinutes(50)
        };
        return options.Cookie.Name + "=" + options.TicketDataFormat.Protect(
            new AuthenticationTicket(new ClaimsPrincipal(identity), properties, IdentityConstants.ApplicationScheme));
    }
}
