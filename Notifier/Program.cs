using Notifier;

using Notifier.Authentication;
using TaskManager.Hosting;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Services.Notifications;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Notifier.Notifications;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTrustedProxyForwarding(builder.Configuration);
// HTTP request-start logs include query strings; keep credentials out of them.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Warning);
builder.Services.AddNotifierAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddNotificationStorage(includeHttpInbox: false).AddNotificationTransport(builder.Configuration);
builder.Services.AddScoped<INotificationRecipientScope, DatabaseNotificationRecipientScope>();
builder.Services.AddSingleton<IInboxRealtimePublisher, InboxRealtimePublisher>();
builder.Services.AddSingleton<NotificationDeliveryCoordinator>();
var transport = builder.Configuration["Notifications:Provider"] ?? "RabbitMQ";
if (transport == "AzureServiceBus") builder.Services.AddHostedService<ServiceBusListenerService>();
else builder.Services.AddHostedService<RabbitMqListenerService>();
builder.Services.AddHostedService(provider => new TaskNotificationScheduler(
    provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<IOptions<ReminderOptions>>(),
    provider.GetRequiredService<IOptions<RabbitMqOptions>>(), provider.GetRequiredService<ILogger<TaskNotificationScheduler>>(),
    provider.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<AiDraftCleanupWorker>();
builder.Services.AddHostedService<NotificationRetentionWorker>();
builder.Services.AddPrivateNotificationSignalR(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<NotifierSqlHealthCheck>("notification-sql", tags: ["ready"])
    .AddCheck<NotificationBrokerHealthCheck>("notification-broker", tags: ["ready"]);
var app = builder.Build();
app.UseTrustedProxyForwarding();

app.UseMiddleware<NotifierOriginGuardMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.MapHub<ChatHub>("/chathub", options => options.CloseOnAuthenticationExpiration = true)
    .RequireAuthorization();

app.MapGet("/", () => "Hello, world! SignalR - ChatHub is working now!").AllowAnonymous();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.Run();
