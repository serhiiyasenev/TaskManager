#nullable enable
using BLL.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BLL.Services.Notifications;

public class TaskNotificationScheduler(
    IServiceScopeFactory scopeFactory, IOptions<ReminderOptions> reminderOptions,
    IOptions<RabbitMqOptions> rabbitMqOptions, ILogger logger, TimeProvider? timeProvider = null) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => throw new NotImplementedException();
    public Task RunOnceAsync(CancellationToken ct = default) => ProcessRemindersAsync(ct);
    protected internal virtual Task ProcessRemindersAsync(CancellationToken ct) => throw new NotImplementedException();
}
