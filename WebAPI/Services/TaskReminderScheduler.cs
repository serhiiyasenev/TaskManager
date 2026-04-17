using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Models.Messaging;
using DAL.Context;
using DAL.Enum;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskEntity = DAL.Entities.Task;

namespace WebAPI.Services;

[ExcludeFromCodeCoverage]
public class TaskReminderScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<ReminderOptions> reminderOptions,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<TaskReminderScheduler> logger)
    : BackgroundService
{
    private const int MaxSupportedOffsetMinutes = 7 * 24 * 60;
    private readonly ReminderOptions _options = reminderOptions.Value;
    private readonly string _reminderQueueName = rabbitMqOptions.Value.ReminderQueueName
        ?? rabbitMqOptions.Value.QueueName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Task reminders disabled via configuration. Scheduler will not start.");
            return;
        }

        var delay = TimeSpan.FromMinutes(Math.Max(1, _options.PollIntervalMinutes));
        logger.LogInformation("Task reminder scheduler started with interval {Interval} minutes", delay.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error during reminder processing");
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Task reminder scheduler stopped.");
    }

    public Task RunOnceAsync(CancellationToken ct = default) => ProcessRemindersAsync(ct);

    protected internal virtual async Task ProcessRemindersAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskContext>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var queue = scope.ServiceProvider.GetRequiredService<IQueueService>();

        var nowUtc = DateTime.UtcNow;
        var reminderWindowMinutes = Math.Max(MaxSupportedOffsetMinutes, Math.Max(0, _options.DefaultReminderOffsetMinutes));
        var minEscalationDelayMinutes = _options.DefaultEscalationDelayMinutes <= 0 ? 0 : 1;
        var reminderDueDateCutoffUtc = nowUtc.AddMinutes(reminderWindowMinutes);
        var escalationDueDateCutoffUtc = nowUtc.AddMinutes(-minEscalationDelayMinutes);
        var tasks = await dbContext.Tasks
            .AsTracking()
            .Include(t => t.Project)
            .Where(t => t.DueDate != null
                        && t.State != TaskState.Done
                        && t.State != TaskState.Canceled
                        && ((t.ReminderEnabled
                             && t.ReminderSentAt == null
                             && t.DueDate <= reminderDueDateCutoffUtc)
                            || (t.EscalationEnabled
                                && t.EscalationSentAt == null
                                && t.DueDate <= escalationDueDateCutoffUtc)))
            .ToListAsync(ct);

        var remindersSent = 0;
        var escalationsSent = 0;

        foreach (var task in tasks)
        {
            var due = task.DueDate!.Value;
            var reminderOffset = task.ReminderOffsetMinutes ?? _options.DefaultReminderOffsetMinutes;
            var escalationDelay = task.EscalationDelayMinutes ?? _options.DefaultEscalationDelayMinutes;

            if (task.ReminderEnabled && task.ReminderSentAt is null && nowUtc >= due.AddMinutes(-reminderOffset))
            {
                var published = await PublishAsync(queue, NotificationEventType.TaskReminder, task, reminderOffset, escalationDelay, ct);
                if (published)
                {
                    task.ReminderSentAt = nowUtc;
                    remindersSent++;
                }
            }

            if (task.EscalationEnabled && task.EscalationSentAt is null && nowUtc >= due.AddMinutes(escalationDelay))
            {
                var published = await PublishAsync(queue, NotificationEventType.TaskOverdueEscalation, task, reminderOffset, escalationDelay, ct);
                if (published)
                {
                    task.EscalationSentAt = nowUtc;
                    escalationsSent++;
                }
            }
        }

        if (remindersSent > 0 || escalationsSent > 0)
        {
            await uow.SaveChangesAsync(ct);
        }

        logger.LogInformation("Reminder scan completed. Sent {Reminders} reminders and {Escalations} escalations.", remindersSent, escalationsSent);
    }

    private async Task<bool> PublishAsync(
        IQueueService queue,
        NotificationEventType type,
        TaskEntity task,
        int reminderOffset,
        int escalationDelay,
        CancellationToken ct)
    {
        var envelope = new TaskNotificationEnvelope
        {
            Type = type,
            Reminder = new TaskReminderNotification(
                task.Id,
                task.Name,
                task.PerformerId,
                task.ProjectId,
                task.Project?.Name,
                task.DueDate!.Value,
                reminderOffset,
                escalationDelay),
            CorrelationId = Guid.NewGuid().ToString("N")
        };

        var payload = JsonSerializer.Serialize(envelope);
        var queueName = _reminderQueueName;

        var published = await queue.PostValue(payload, queueName, ct);
        if (!published)
        {
            logger.LogWarning("Failed to publish {Type} for task {TaskId}", type, task.Id);
        }

        return published;
    }
}
