using Microsoft.Extensions.Configuration;

namespace Client.Services;

public sealed record ClientConnectionOptions(Uri ApiBaseUrl, Uri HubUrl)
{
    public static ClientConnectionOptions FromConfiguration(IConfiguration configuration) => new(
        Parse(configuration["Api:BaseUrl"] ?? "https://localhost:7151/api/", true),
        Parse(configuration["SignalR:HubUrl"] ?? "https://localhost:7268/chathub", false));

    private static Uri Parse(string value, bool trailingSlash)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Client endpoints must be HTTP(S) URLs without credentials, query or fragment.");
        return trailingSlash && !uri.AbsoluteUri.EndsWith('/') ? new Uri(uri.AbsoluteUri + "/") : uri;
    }
}
