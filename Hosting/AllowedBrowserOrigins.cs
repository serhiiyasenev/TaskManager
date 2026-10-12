using Microsoft.Extensions.Configuration;

namespace TaskManager.Hosting;

// Shared origin contract for canonical browser traffic and an explicit release
// candidate. Recovery/confirmation links always retain the canonical origin.
public static class AllowedBrowserOrigins
{
    public static IReadOnlyList<Uri> Read(IConfiguration configuration, bool development)
    {
        var publicOrigin = configuration["BrowserAuth:PublicOrigin"];
        if (string.IsNullOrWhiteSpace(publicOrigin) && !development)
            throw new InvalidOperationException("BrowserAuth:PublicOrigin must be explicitly configured.");
        var additional = configuration.GetSection("BrowserAuth:AdditionalAllowedOrigins").GetChildren().ToArray();
        if (additional.Length > 2)
            throw new InvalidOperationException("BrowserAuth permits at most two explicit additional origins.");
        var origins = new List<Uri> { Parse(publicOrigin ?? "https://localhost", development) };
        foreach (var entry in additional)
        {
            var origin = Parse(entry.Value, development);
            if (origins.Any(existing => string.Equals(existing.GetLeftPart(UriPartial.Authority),
                origin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("BrowserAuth origins must be distinct.");
            origins.Add(origin);
        }
        return origins.AsReadOnly();
    }

    private static Uri Parse(string? value, bool development)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) || !origin.IsWellFormedOriginalString()
            || !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != "/"
            || origin.Host.Contains('*') || origin.HostNameType == UriHostNameType.Unknown
            || (origin.Scheme != Uri.UriSchemeHttps && !(development && origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback)))
            throw new InvalidOperationException("BrowserAuth origins must be explicit HTTPS roots without credentials, query, fragment or wildcard.");
        return origin;
    }
}
