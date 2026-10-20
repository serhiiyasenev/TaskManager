using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Client.Services;

public sealed class ConsoleSession(HttpClient client)
{
    private string? _accessToken;
    public HttpClient Client { get; } = client;
    public string? AccessToken => Volatile.Read(ref _accessToken);
    public int? UserId { get; private set; }
    public bool IsAuthenticated => AccessToken is not null;

    public async Task<bool> LoginAsync(string email, string password, CancellationToken token = default)
    {
        Logout();
        using var response = await Client.PostAsJsonAsync("Users/Login", new { email, password }, token);
        if (!response.IsSuccessStatusCode) return false;
        ConsoleLoginResponse? result;
        try { result = await response.Content.ReadFromJsonAsync<ConsoleLoginResponse>(cancellationToken: token); }
        catch (JsonException) { return false; }
        if (result is not { Id: > 0 } || string.IsNullOrWhiteSpace(result.Token) || result.Token.Length > 16384) return false;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token);
        UserId = result.Id;
        Volatile.Write(ref _accessToken, result.Token);
        return true;
    }

    public void Logout()
    {
        Volatile.Write(ref _accessToken, null);
        UserId = null;
        Client.DefaultRequestHeaders.Authorization = null;
    }

    private sealed record ConsoleLoginResponse(int Id, string Token);
}
