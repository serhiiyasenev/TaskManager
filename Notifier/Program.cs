using Notifier;

using Notifier.Authentication;

var builder = WebApplication.CreateBuilder(args);
// HTTP request-start logs include query strings; keep credentials out of them.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Warning);
builder.Services.AddNotifierAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<RabbitMqListenerService>();
builder.Services.AddSignalR(options => options.EnableDetailedErrors = false);
var app = builder.Build();

app.UseMiddleware<NotifierOriginGuardMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<ChatHub>("/chathub", options => options.CloseOnAuthenticationExpiration = true)
    .RequireAuthorization();

app.MapGet("/", () => "Hello, world! SignalR - ChatHub is working now!").AllowAnonymous();

app.Run();
