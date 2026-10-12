using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TaskManager.Hosting;

namespace WebAPI.Authentication;

/// <summary>Browser credentials and all cookie authenticated mutations require a CSRF token.</summary>
public sealed class BrowserAntiforgeryFilter(IAntiforgery antiforgery, IConfiguration configuration,
    IHostEnvironment environment) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
            HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method))
        {
            return;
        }

        var isBrowserAccountEndpoint = request.Path.StartsWithSegments("/api/auth");
        // The selector forwards an explicit bearer header only to JWT. An invalid JWT
        // cannot fall back to a valid browser cookie and bypass this check.
        var usesBearer = BrowserAuthenticationExtensions.HasBearerHeader(request);
        if (!isBrowserAccountEndpoint && (usesBearer || context.HttpContext.User.Identity?.IsAuthenticated != true))
        {
            return;
        }

        if (request.Headers.TryGetValue("Origin", out var header) &&
            (header.Count != 1 || !IsAllowedOrigin(header[0])))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Browser origin is not allowed.",
                Detail = "Use an explicitly configured TaskManager browser origin."
            }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid antiforgery token.",
                Detail = "Refresh your session and retry the operation."
            });
        }
    }

    private bool IsAllowedOrigin(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) || origin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
            return false;
        return AllowedBrowserOrigins.Read(configuration, environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            .Contains(origin);
    }
}
