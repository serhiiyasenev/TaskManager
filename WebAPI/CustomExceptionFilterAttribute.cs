using System.Net;
using BLL.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WebAPI;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class CustomExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        var statusCode = context.Exception.ParseException();

        context.HttpContext.Response.ContentType = "application/json";
        context.HttpContext.Response.StatusCode = (int)statusCode;
        context.Result = new JsonResult(new
        {
            error = context.Exception.Message,
            code = statusCode
        });
    }
}

public static class ExceptionFilterExtensions
{
    public static HttpStatusCode ParseException(this Exception exception)
    {
        return exception switch
        {
            NotFoundException _ => HttpStatusCode.NotFound,
            CanNotDeleteException _ => HttpStatusCode.Conflict,
            _ => HttpStatusCode.InternalServerError
        };
    }
}