using BLL.Configuration;
using BLL.Services.Notifications;
using Microsoft.Extensions.Options;

namespace WebAPI.Services;

// Constructor compatibility for legacy tests. This type is not registered as
// an API hosted service; the single shared implementation runs in Notifier.
public class TaskReminderScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<ReminderOptions> reminderOptions,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<TaskReminderScheduler> logger,
    TimeProvider? timeProvider = null)
    : TaskNotificationScheduler(scopeFactory, reminderOptions, rabbitMqOptions, logger, timeProvider);
