using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WebAPI;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class CustomExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        var statusCode = context.Exception.ParseException();

        context.HttpContext.Response.ContentType = "application/problem+json";
        context.HttpContext.Response.StatusCode = (int)statusCode;
        var problem = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = statusCode == System.Net.HttpStatusCode.InternalServerError ? "internal_error" : statusCode.ToString(),
            Detail = statusCode == System.Net.HttpStatusCode.InternalServerError
                ? "Unexpected server error." : context.Exception.Message,
            Extensions = { ["traceId"] = context.HttpContext.TraceIdentifier }
        };
        context.Result = new ObjectResult(problem) { StatusCode = (int)statusCode };
        context.ExceptionHandled = true;
    }
}
