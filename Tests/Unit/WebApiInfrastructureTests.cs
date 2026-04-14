using System.Net;
using System.Text.Json;
using BLL.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using WebAPI;
using WebAPI.Middleware;
using Xunit;

namespace Tests.Unit;

public class WebApiInfrastructureTests
{
    [Fact]
    public void ParseException_MapsKnownAndUnknownExceptions()
    {
        Assert.Equal(HttpStatusCode.NotFound, new NotFoundException("x").ParseException());
        Assert.Equal(HttpStatusCode.Conflict, new CanNotDeleteException("x").ParseException());
        Assert.Equal(HttpStatusCode.InternalServerError, new Exception("x").ParseException());
    }

    [Fact]
    public void CustomExceptionFilterAttribute_SetsJsonErrorResponse()
    {
        var httpContext = new DefaultHttpContext();
        var context = new ExceptionContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            [])
        {
            Exception = new NotFoundException("missing")
        };
        var filter = new CustomExceptionFilterAttribute();

        filter.OnException(context);

        Assert.Equal("application/json", httpContext.Response.ContentType);
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
        Assert.IsType<JsonResult>(context.Result);
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_WhenNoException_CallsNext()
    {
        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };
        var middleware = new ExceptionHandlingMiddleware(next, Mock.Of<ILogger<ExceptionHandlingMiddleware>>());

        await middleware.Invoke(new DefaultHttpContext());

        Assert.True(called);
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_WhenException_WritesProblemDetails()
    {
        RequestDelegate next = _ => throw new InvalidOperationException("boom");
        var middleware = new ExceptionHandlingMiddleware(next, Mock.Of<ILogger<ExceptionHandlingMiddleware>>());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.Invoke(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json; charset=utf-8", context.Response.ContentType);

        context.Response.Body.Position = 0;
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("internal_error", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(context.TraceIdentifier, doc.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public void UseGlobalExceptionHandling_ReturnsApplicationBuilder()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);

        var result = app.UseGlobalExceptionHandling();

        Assert.Same(app, result);
    }
}
