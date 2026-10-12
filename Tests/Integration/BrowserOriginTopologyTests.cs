using Microsoft.Extensions.Configuration;
using TaskManager.Hosting;
using Xunit;

namespace Tests.Integration;

public sealed class BrowserOriginTopologyTests
{
    [Fact]
    public void CanonicalAndExplicitCandidate_AreDistinctBoundedOrigins()
    {
        var origins = AllowedBrowserOrigins.Read(Config("https://candidate---taskmanager-web.example.test"), false);
        Assert.Equal(new[] { "https://taskmanager.example.test", "https://candidate---taskmanager-web.example.test" },
            origins.Select(value => value.GetLeftPart(UriPartial.Authority)));
    }

    [Theory]
    [InlineData("http://candidate.example.test")]
    [InlineData("https://user:password@candidate.example.test")]
    [InlineData("https://candidate.example.test/api")]
    [InlineData("https://candidate.example.test/?access_token=synthetic")]
    [InlineData("https://candidate.example.test/#fragment")]
    [InlineData("https://*.example.test")]
    [InlineData("https://taskmanager.example.test/")]
    public void UnsafeOrDuplicateCandidate_FailsClosed(string candidate)
    {
        var error = Assert.Throws<InvalidOperationException>(() => AllowedBrowserOrigins.Read(Config(candidate), false));
        Assert.Contains("BrowserAuth", error.Message);
        Assert.DoesNotContain("password", error.Message);
        Assert.DoesNotContain("access_token", error.Message);
    }

    [Fact]
    public void MoreThanTwoCandidateOrigins_IsRejected()
    {
        var configuration = Config("https://first.example.test");
        configuration["BrowserAuth:AdditionalAllowedOrigins:1"] = "https://second.example.test";
        configuration["BrowserAuth:AdditionalAllowedOrigins:2"] = "https://third.example.test";
        Assert.Throws<InvalidOperationException>(() => AllowedBrowserOrigins.Read(configuration, false));
    }

    [Fact]
    public void TestingPermitsOnlyLoopbackHttp_ProductionRequiresHttps()
    {
        var configuration = Config("http://127.0.0.1:7151");
        Assert.Equal("http://127.0.0.1:7151", AllowedBrowserOrigins.Read(configuration, true)[1].GetLeftPart(UriPartial.Authority));
        Assert.Throws<InvalidOperationException>(() => AllowedBrowserOrigins.Read(configuration, false));
    }

    private static IConfigurationRoot Config(string candidate) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["BrowserAuth:PublicOrigin"] = "https://taskmanager.example.test",
        ["BrowserAuth:AdditionalAllowedOrigins:0"] = candidate
    }).Build();
}
