using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WebAPI.Authentication;

/// <summary>Browser credentials and all cookie authenticated mutations require a CSRF token.</summary>
public sealed class BrowserAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
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
}
