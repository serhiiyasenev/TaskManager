#nullable enable
using BLL.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DAL.Context;
using NotificationOutboxMessage = DAL.Entities.NotificationOutboxMessage;
using NotificationOutboxStatus = DAL.Entities.NotificationOutboxStatus;
using NotificationOutboxKind = DAL.Entities.NotificationOutboxKind;
using DAL.Enum;
using BLL.Models.Messaging;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services.Notifications;

public sealed class TaskNotificationDispatcher(
    IServiceScopeFactory scopeFactory, ILogger<TaskNotificationDispatcher> logger,
    TimeProvider? timeProvider = null) : ITaskNotificationDispatcher
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    public async Task<bool> TryDispatchAsync(Guid messageId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskContext>();
        try
        {
            var message = await db.NotificationOutbox.SingleOrDefaultAsync(value => value.Id == messageId, ct);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (message is null) return false;
            if (message.Status == NotificationOutboxStatus.Published) return true;
            if (message.Status != NotificationOutboxStatus.Pending || message.LockedUntilUtc > now) return false;
            message.LockId = Guid.NewGuid();
            message.LockedUntilUtc = now.Add(Lease);
            await db.SaveChangesAsync(ct);

            var task = await db.Tasks.AsTracking().Include(value => value.Project).SingleOrDefaultAsync(value => value.Id == message.TaskId, ct);
            ValidatedNotificationPayload? parsed = null;
            try { parsed = NotificationPayloadValidator.Parse(message.Id, message.Payload); }
            catch (InvalidDataException) { /* Invalid persisted intent is canceled, never rewritten. */ }
            var valid = task is not null && parsed is not null && parsed.TaskId == message.TaskId
                && parsed.ProjectId == task.ProjectId && parsed.PerformerId == task.PerformerId
                && KindMatches(message, parsed.Type) && !string.IsNullOrWhiteSpace(message.QueueName);
            if (valid && task is not null && parsed is not null)
            {
                var member = await db.Users.AsNoTracking().Where(user => user.Id == parsed.PerformerId)
                    .Select(user => new { user.TeamId }).SingleOrDefaultAsync(ct);
                valid = member is not null && task.Project is not null
                    && task.Project.TeamId > 0 && member.TeamId == task.Project.TeamId;
                if (message.Kind == NotificationOutboxKind.Reminder)
                {
                    var occurrence = message.IsEscalation ? task.EscalationNotificationId : task.ReminderNotificationId;
                    var enabled = message.IsEscalation ? task.EscalationEnabled : task.ReminderEnabled;
                    valid = valid && occurrence == message.Id && enabled
                        && task.State is not (TaskState.Done or TaskState.Canceled)
                        && task.DueDate.HasValue && parsed.Envelope.Reminder is not null
                        && NotificationPayloadValidator.AsUtc(task.DueDate.Value) == parsed.Envelope.Reminder.DueDateUtc;
                }
            }
            if (!valid)
            {
                message.Status = NotificationOutboxStatus.Canceled;
                message.LockId = null; message.LockedUntilUtc = null;
                await db.SaveChangesAsync(ct);
                return false;
            }

            using var publishTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            publishTimeout.CancelAfter(TimeSpan.FromMinutes(2));
            var published = await scope.ServiceProvider.GetRequiredService<IQueueService>()
                .PostValue(message.Payload, message.QueueName, publishTimeout.Token);
            now = _clock.GetUtcNow().UtcDateTime;
            if (published)
            {
                message.Status = NotificationOutboxStatus.Published;
                message.PublishedAtUtc = now;
                if (message.Kind == NotificationOutboxKind.Reminder)
                {
                    if (message.IsEscalation) task!.EscalationSentAt = now;
                    else task!.ReminderSentAt = now;
                }
            }
            message.LockId = null;
            message.LockedUntilUtc = published ? null : now.AddMinutes(1);
            await db.SaveChangesAsync(ct);
            return published;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            logger.LogInformation("Notification intent {MessageId} changed concurrently; claim left for recovery.", messageId);
            return false;
        }
        catch (Exception)
        {
            // A publish/save failure leaves its existing five-minute claim for
            // recovery with the exact same envelope and stable ID.
            db.ChangeTracker.Clear();
            logger.LogWarning("Notification intent {MessageId} remains pending after an uncertain dispatch.", messageId);
            return false;
        }
    }

    private static bool KindMatches(NotificationOutboxMessage message, NotificationEventType type) => message.Kind switch
    {
        NotificationOutboxKind.Reminder => type == (message.IsEscalation ? NotificationEventType.TaskOverdueEscalation : NotificationEventType.TaskReminder),
        NotificationOutboxKind.TaskCreated => type == NotificationEventType.TaskCreated,
        NotificationOutboxKind.ExecutedTaskCompleted => type == NotificationEventType.ExecutedTaskCompleted,
        _ => false
    };
}
