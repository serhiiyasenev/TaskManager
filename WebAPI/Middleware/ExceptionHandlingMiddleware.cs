using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted) throw;

            logger.LogError(ex, $"Unhandled exception. TraceId={context.TraceIdentifier}");

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "internal_error",
                Detail = "Unexpected server error.",
                Extensions =
                {
                    ["traceId"] = context.TraceIdentifier,
                    ["timestamp"] = DateTimeOffset.UtcNow
                }
            };

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json; charset=utf-8";

            var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}