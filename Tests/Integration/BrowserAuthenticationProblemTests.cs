using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.Integration;

public sealed class BrowserAuthenticationProblemTests
{
    [Theory]
    [InlineData("private-account@example.test")]
    [InlineData("2147483648")]
    [InlineData("999999999999999999999999999999")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task MalformedConfirmationUserId_ReturnsSafe400WithoutAccountOrTokenDetails(string userId)
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());

        const string privateToken = "private-confirmation-token";
        var response = await client.PostAsJsonAsync("/api/auth/confirm-email", new { userId, token = privateToken });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.False(problem.TryGetProperty("stackTrace", out _));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-account@example.test", body);
        Assert.DoesNotContain(privateToken, body);
    }

    [Theory]
    [InlineData("login", HttpStatusCode.Unauthorized)]
    [InlineData("reset-password", HttpStatusCode.BadRequest)]
    public async Task InvalidBrowserCredentials_ReturnSafeProblemDetails(string action, HttpStatusCode expected)
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());

        var response = await client.PostAsJsonAsync($"/api/auth/{action}", new
        {
            email = "missing@example.test", password = "TestPassword123!", token = "invalid-token", newPassword = "ReplacementPassword123!"
        });

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.False(problem.TryGetProperty("stackTrace", out _));
        Assert.DoesNotContain("missing@example.test", await response.Content.ReadAsStringAsync());
    }
}
