namespace Notifier.Authentication;

public sealed class NotifierOriginGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly Uri _publicOrigin;

    public NotifierOriginGuardMiddleware(RequestDelegate next, IConfiguration configuration, IHostEnvironment environment)
    {
        _next = next;
        var configuredOrigin = configuration["BrowserAuth:PublicOrigin"];
        var development = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        if (!development && string.IsNullOrWhiteSpace(configuredOrigin))
            throw new InvalidOperationException("Notifier BrowserAuth:PublicOrigin must be explicitly configured in production.");
        if (!Uri.TryCreate(configuredOrigin ?? "https://localhost", UriKind.Absolute, out var origin) ||
            !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != "/" ||
            (origin.Scheme != Uri.UriSchemeHttps && !(development && origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback)))
            throw new InvalidOperationException("Notifier BrowserAuth:PublicOrigin must be an HTTPS origin without path, credentials, query or fragment.");
        _publicOrigin = origin;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // CORS does not protect WebSockets. A browser's Origin must match the
        // configured public gateway; native console clients omit this header.
        if (context.Request.Path.StartsWithSegments("/chathub") && context.Request.Headers.TryGetValue("Origin", out var values))
        {
            if (values.Count != 1 || !Uri.TryCreate(values[0], UriKind.Absolute, out var origin) ||
                !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) ||
                !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != "/" ||
                !string.Equals(origin.GetLeftPart(UriPartial.Authority), _publicOrigin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
        }
        return _next(context);
    }
}
