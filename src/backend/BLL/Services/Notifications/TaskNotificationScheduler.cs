#nullable enable
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Models.Messaging;
using DAL.Context;
using DAL.Enum;
using NotificationOutboxMessage = DAL.Entities.NotificationOutboxMessage;
using NotificationOutboxStatus = DAL.Entities.NotificationOutboxStatus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskEntity = DAL.Entities.Task;

namespace BLL.Services.Notifications;

public class TaskNotificationScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<ReminderOptions> reminderOptions,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger logger,
    TimeProvider? timeProvider = null)
    : BackgroundService
{
    private DateTimeOffset _nextReminderScan = DateTimeOffset.MinValue;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private const int BatchSize = 100;
    private const int MaxSupportedOffsetMinutes = 7 * 24 * 60;
    private readonly ReminderOptions _options = reminderOptions.Value;
    private readonly string _reminderQueueName = rabbitMqOptions.Value.ReminderQueueName
        ?? rabbitMqOptions.Value.QueueName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(5);
        logger.LogInformation("Task notification worker started; reminder scanning enabled: {Enabled}.", _options.Enabled);

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
            catch (Exception)
            {
                logger.LogError("Notification worker pass failed; it will retry on the next poll.");
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

        logger.LogInformation("Task notification worker stopped.");
    }

    public Task RunOnceAsync(CancellationToken ct = default)
    {
        _nextReminderScan = DateTimeOffset.MinValue;
        return ProcessRemindersAsync(ct);
    }

    protected internal virtual async Task ProcessRemindersAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_options.Enabled && _clock.GetUtcNow() >= _nextReminderScan)
        {
            await EnqueueDueNotificationsAsync(ct);
            _nextReminderScan = _clock.GetUtcNow().AddMinutes(Math.Clamp(_options.PollIntervalMinutes, 1, 1440));
        }
        await DispatchPendingNotificationsAsync(ct);
    }

    private async Task EnqueueDueNotificationsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskContext>();
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var reminderOffset = Math.Clamp(_options.DefaultReminderOffsetMinutes, 1, MaxSupportedOffsetMinutes);
        var escalationDelay = Math.Clamp(_options.DefaultEscalationDelayMinutes, 1, MaxSupportedOffsetMinutes);
        var tasks = await db.Tasks.AsTracking().Include(t => t.Project)
            .Where(t => t.DueDate != null && t.State != TaskState.Done && t.State != TaskState.Canceled
                && ((t.ReminderEnabled && t.ReminderSentAt == null && t.ReminderNotificationId == null
                    && t.DueDate.Value.AddMinutes(-(t.ReminderOffsetMinutes ?? reminderOffset)) <= nowUtc)
                || (t.EscalationEnabled && t.EscalationSentAt == null && t.EscalationNotificationId == null
                    && t.DueDate.Value.AddMinutes(t.EscalationDelayMinutes ?? escalationDelay) <= nowUtc)))
            .OrderBy(t => t.Id).Take(BatchSize).ToListAsync(ct);

        foreach (var task in tasks)
        {
            var offset = Math.Clamp(task.ReminderOffsetMinutes ?? reminderOffset, 1, MaxSupportedOffsetMinutes);
            var delay = Math.Clamp(task.EscalationDelayMinutes ?? escalationDelay, 1, MaxSupportedOffsetMinutes);
            if (task.ReminderEnabled && task.ReminderSentAt is null && task.ReminderNotificationId is null
                && nowUtc >= task.DueDate!.Value.AddMinutes(-offset))
                task.ReminderNotificationId = Enqueue(db, task, false, offset, delay, nowUtc);
            if (task.EscalationEnabled && task.EscalationSentAt is null && task.EscalationNotificationId is null
                && nowUtc >= task.DueDate!.Value.AddMinutes(delay))
                task.EscalationNotificationId = Enqueue(db, task, true, offset, delay, nowUtc);
        }

        try
        {
            // One relational transaction stores both the occurrence IDs and
            // envelopes. Nothing is published before this commit succeeds.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            logger.LogInformation("Reminder scan raced with another scan or task update; retrying next poll.");
        }
    }

    private Guid Enqueue(TaskContext db, TaskEntity task, bool escalation, int offset, int delay, DateTime nowUtc)
    {
        var id = Guid.NewGuid();
        var envelope = new TaskNotificationEnvelope
        {
            Type = escalation ? NotificationEventType.TaskOverdueEscalation : NotificationEventType.TaskReminder,
            CorrelationId = id.ToString("N"),
            Reminder = new TaskReminderNotification(task.Id, task.Name, task.PerformerId, task.ProjectId,
                task.Project?.Name, task.DueDate!.Value, offset, delay)
        };
        db.NotificationOutbox.Add(new NotificationOutboxMessage
        {
            Id = id, TaskId = task.Id, IsEscalation = escalation, QueueName = _reminderQueueName,
            Payload = JsonSerializer.Serialize(envelope), CreatedAtUtc = nowUtc
        });
        return id;
    }

    private async Task DispatchPendingNotificationsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskContext>();
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var ids = await db.NotificationOutbox.AsNoTracking()
            .Where(message => message.Status == NotificationOutboxStatus.Pending
                && (message.LockedUntilUtc == null || message.LockedUntilUtc <= nowUtc))
            .OrderBy(message => message.CreatedAtUtc).ThenBy(message => message.Id)
            .Select(message => message.Id).Take(BatchSize).ToListAsync(ct);
        var dispatcher = scope.ServiceProvider.GetRequiredService<ITaskNotificationDispatcher>();
        foreach (var id in ids) await dispatcher.TryDispatchAsync(id, ct);
    }
}
