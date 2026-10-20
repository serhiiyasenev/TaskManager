extern alias NotifierLib;
using System.Security.Claims;
using BLL.Interfaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using TaskManager.Hosting.Cloud;
using WebAPI.Authentication;
using Xunit;

namespace Tests.Cloud;

public sealed class CloudAuthenticationRegistrationTests
{
    [Fact]
    public void ApiAndNotifierUseTheSameNativeAzureKeyRing()
    {
        using var fixture = new NativeKeyRingFixture();
        var config = Configuration();
        var apiServices = Services(config, fixture);
        apiServices.AddBrowserAuthentication(config, new CloudTestEnvironment());
        using var api = apiServices.BuildServiceProvider();
        _ = api.GetRequiredService<IOptions<BrowserAuthenticationOptions>>().Value;
        var token = api.GetRequiredService<IDataProtectionProvider>().CreateProtector("shared-identity-token").Protect("synthetic-token");

        var notifierServices = Services(config, fixture);
        NotifierLib::Notifier.Authentication.NotifierAuthenticationExtensions.AddNotifierAuthentication(
            notifierServices, config, new CloudTestEnvironment());
        using var notifier = notifierServices.BuildServiceProvider();
        Assert.Equal("synthetic-token", notifier.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("shared-identity-token").Unprotect(token));
        Assert.Contains("encryptedKey", fixture.Xml);
    }

    [Fact]
    public void AzureEmailSelectionDoesNotRequireSmtpConfigurationOrRegisterSmtpSender()
    {
        var config = Configuration();
        using var fixture = new NativeKeyRingFixture();
        var services = Services(config, fixture);
        services.AddBrowserAuthentication(config, new CloudTestEnvironment());
        using var provider = services.BuildServiceProvider();
        Assert.Equal("AzureCommunication", provider.GetRequiredService<IOptions<AccountEmailOptions>>().Value.Provider);
        Assert.IsType<AzureCommunicationAccountEmailSender>(provider.GetRequiredService<IAccountEmailSender>());
    }

    [Fact]
    public void CorsAllowsOnlyExplicitCanonicalAndCandidateOriginsWithCredentials()
    {
        var config = Configuration();
        using var fixture = new NativeKeyRingFixture();
        var services = Services(config, fixture);
        services.AddBrowserAuthentication(config, new CloudTestEnvironment());
        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy("Frontend");
        Assert.NotNull(policy);
        Assert.Equal(["https://public.example.test", "https://candidate.example.test"], policy.Origins);
        Assert.True(policy.SupportsCredentials);
        Assert.False(policy.AllowAnyOrigin);
        Assert.False(policy.IsOriginAllowed("https://foreign.example.test"));
    }

    [Theory]
    [InlineData("https://public.example.test", true)]
    [InlineData("https://candidate.example.test", true)]
    [InlineData("https://foreign.example.test", false)]
    [InlineData("null", false)]
    [InlineData("https://public.example.test?token=secret", false)]
    public async Task CookieMutationRequiresAnAllowedOriginEvenWithValidCsrfToken(string origin, bool allowed)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration());
        services.AddSingleton<IHostEnvironment>(new CloudTestEnvironment());
        using var provider = services.BuildServiceProvider();
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);
        var filter = ActivatorUtilities.CreateInstance<BrowserAntiforgeryFilter>(provider, antiforgery.Object);
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Request.Method = "POST";
        http.Request.Path = "/api/tasks";
        http.Request.Headers.Origin = origin;
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "101")], "cookie"));
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);

        await filter.OnAuthorizationAsync(context);

        if (allowed) Assert.Null(context.Result);
        else Assert.Equal(403, Assert.IsType<ObjectResult>(context.Result).StatusCode);
        antiforgery.Verify(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()), allowed ? Times.Once() : Times.Never());
    }

    private static ServiceCollection Services(IConfiguration config, NativeKeyRingFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(config);
        services.AddSingleton<IHostEnvironment>(new CloudTestEnvironment());
        services.AddSingleton<ICloudDataProtectionClients>(fixture);
        return services;
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["BrowserAuth:PublicOrigin"] = "https://public.example.test",
        ["BrowserAuth:AdditionalAllowedOrigins:0"] = "https://candidate.example.test",
        ["DataProtection:Provider"] = "AzureBlobKeyVault",
        ["DataProtection:BlobUri"] = "https://taskmanager-test.blob.core.windows.net/keys/keyring.xml",
        ["DataProtection:KeyVaultKeyUri"] = "https://taskmanager-test.vault.azure.net/keys/keyring",
        ["DataProtection:ManagedIdentityClientId"] = "5992d8df-3e71-401e-9864-ccfa1cfdac22",
        ["Email:Provider"] = "AzureCommunication",
        ["Email:AzureCommunicationEndpoint"] = "https://synthetic.communication.azure.com",
        ["Email:FromAddress"] = "verified@sender.example.test",
        ["Email:SmtpHost"] = "", ["Email:SmtpPort"] = "0",
        ["Jwt:Key"] = "synthetic-private-test-key", ["Jwt:Issuer"] = "synthetic", ["Jwt:Audience"] = "synthetic"
    }).Build();
}
