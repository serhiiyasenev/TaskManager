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

namespace WebAPI.Services;

public class TaskReminderScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<ReminderOptions> reminderOptions,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<TaskReminderScheduler> logger,
    TimeProvider? timeProvider = null)
    : BackgroundService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private const int BatchSize = 100;
    private static readonly TimeSpan DispatchLease = TimeSpan.FromMinutes(5);
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
        await EnqueueDueNotificationsAsync(ct);
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
            .Where(m => m.Status == NotificationOutboxStatus.Pending
                && (m.LockedUntilUtc == null || m.LockedUntilUtc <= nowUtc))
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
            .Select(m => m.Id).Take(BatchSize).ToListAsync(ct);
        foreach (var id in ids)
            await DispatchAsync(id, ct);
    }

    private async Task DispatchAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskContext>();
        try
        {
            var message = await db.NotificationOutbox.SingleOrDefaultAsync(m => m.Id == id, ct);
            var nowUtc = _clock.GetUtcNow().UtcDateTime;
            if (message is null || message.Status != NotificationOutboxStatus.Pending || message.LockedUntilUtc > nowUtc)
                return;

            // LockId + Status form a compare-and-swap claim across processes.
            // If this process dies, the lease expires and another worker retries.
            message.LockId = Guid.NewGuid();
            message.LockedUntilUtc = nowUtc.Add(DispatchLease);
            await db.SaveChangesAsync(ct);

            var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == message.TaskId, ct);
            var currentId = message.IsEscalation ? task?.EscalationNotificationId : task?.ReminderNotificationId;
            var enabled = message.IsEscalation ? task?.EscalationEnabled : task?.ReminderEnabled;
            if (task is null || currentId != message.Id || enabled != true
                || task.State is TaskState.Done or TaskState.Canceled)
            {
                message.Status = NotificationOutboxStatus.Canceled;
                message.LockId = null;
                message.LockedUntilUtc = null;
                await db.SaveChangesAsync(ct);
                return;
            }

            var queue = scope.ServiceProvider.GetRequiredService<IQueueService>();
            // Bound the complete publish operation below the five-minute lease,
            // including publisher retry/backoff and broker-confirm waits.
            using var publishCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            publishCts.CancelAfter(TimeSpan.FromMinutes(2));
            var published = await queue.PostValue(message.Payload, message.QueueName, publishCts.Token);
            nowUtc = _clock.GetUtcNow().UtcDateTime;
            if (published)
            {
                message.Status = NotificationOutboxStatus.Published;
                message.PublishedAtUtc = nowUtc;
                if (message.IsEscalation) task.EscalationSentAt = nowUtc;
                else task.ReminderSentAt = nowUtc;
            }
            message.LockId = null;
            message.LockedUntilUtc = published ? null : nowUtc.AddMinutes(1);
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            logger.LogInformation("Outbox entry {MessageId} changed concurrently; leaving it for a later poll.", id);
        }
        catch (Exception ex)
        {
            // A claim already committed to the DB is deliberately not marked
            // successful after an ambiguous send/save. Recovery reuses its ID.
            db.ChangeTracker.Clear();
            logger.LogWarning(ex, "Outbox entry {MessageId} remains pending and will be retried after its lease expires.", id);
        }
    }
}
