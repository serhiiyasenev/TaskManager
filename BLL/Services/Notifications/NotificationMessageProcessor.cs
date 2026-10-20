#nullable enable
using System.Data;
using BLL.Interfaces;
using BLL.Models.Messaging;
using DAL.Context;
using DAL.Entities;
using DAL.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BLL.Services.Notifications;

public sealed class NotificationMessageProcessor(TaskContext db, TimeProvider clock,
    ILogger<NotificationMessageProcessor> logger) : INotificationMessageProcessor
{
    public async Task<NotificationProcessingResult> ProcessAsync(Guid messageId, string immutablePayload, CancellationToken ct = default)
    {
        var payload = NotificationPayloadValidator.Parse(messageId, immutablePayload);
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("The processor requires its own short transaction.");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var previous = await db.NotificationProcessingMarkers.AsNoTracking().SingleOrDefaultAsync(value => value.MessageId == messageId, ct);
                if (previous is not null)
                {
                    if (!previous.CanonicalPayloadHash.Equals(payload.CanonicalPayloadHash, StringComparison.Ordinal))
                        throw new InvalidDataException("A stable notification ID has a different immutable payload.");
                    await transaction.CommitAsync(ct);
                    return new(messageId, null, false, previous.Disposition == NotificationProcessingDisposition.Suppressed);
                }
                var now = clock.GetUtcNow().UtcDateTime;
                var candidate = await ReadCurrentCandidateAsync(payload, now, ct);
                var marker = new NotificationProcessingMarker { MessageId = messageId,
                    CanonicalPayloadHash = payload.CanonicalPayloadHash, ProcessedAtUtc = now,
                    Disposition = candidate is null ? NotificationProcessingDisposition.Suppressed : NotificationProcessingDisposition.Delivered };
                db.NotificationProcessingMarkers.Add(marker);
                UserNotification? notification = null;
                if (candidate is not null)
                {
                    notification = new UserNotification { Id = messageId, OwnerId = payload.PerformerId, Kind = (int)payload.Type,
                        TaskId = payload.TaskId, ProjectId = payload.ProjectId, TaskName = candidate.TaskName,
                        ProjectName = candidate.ProjectName, OccurredAtUtc = candidate.OccurredAtUtc };
                    db.UserNotifications.Add(notification);
                }
                await db.SaveChangesAsync(ct);
                // Serializable holds current recipient/project/task reads through
                // commit. The final check also rejects same-context mutations.
                if (notification is not null && await ReadCurrentCandidateAsync(payload, now, ct) is null)
                {
                    db.UserNotifications.Remove(notification);
                    marker.Disposition = NotificationProcessingDisposition.Suppressed;
                    await db.SaveChangesAsync(ct);
                    notification = null;
                }
                await transaction.CommitAsync(ct);
                return new(messageId, notification?.OwnerId, notification is not null, marker.Disposition == NotificationProcessingDisposition.Suppressed);
            }
            catch (Exception exception) when (NotificationSqlErrors.IsDeadlock(exception) || NotificationSqlErrors.IsDuplicateKey(exception))
            {
                await NotificationSqlErrors.RollbackAsync(transaction);
                db.ChangeTracker.Clear();
                logger.LogWarning("Notification processing conflicted with another write (attempt {Attempt}).", attempt + 1);
                if (attempt == 1) throw new IOException("The notification transaction conflicted with another write.");
            }
            catch
            {
                await NotificationSqlErrors.RollbackAsync(transaction);
                db.ChangeTracker.Clear();
                throw;
            }
        }
        throw new IOException("The notification transaction conflicted with another write.");
    }

    private async Task<Candidate?> ReadCurrentCandidateAsync(ValidatedNotificationPayload payload, DateTime now, CancellationToken ct)
    {
        var intent = await db.NotificationOutbox.AsNoTracking().SingleOrDefaultAsync(value => value.Id == payload.MessageId, ct);
        // Deleting a task cascades its pending outbox intent. A valid late
        // delivery still receives a durable suppressed marker.
        if (intent is null) return null;
        var persisted = NotificationPayloadValidator.Parse(intent.Id, intent.Payload);
        if (persisted.CanonicalPayloadHash != payload.CanonicalPayloadHash || intent.TaskId != payload.TaskId
            || !MatchesKind(intent, payload.Type)) throw new InvalidDataException("The notification does not match its persisted intent.");
        if (intent.Status == NotificationOutboxStatus.Canceled || NotificationPayloadValidator.AsUtc(intent.CreatedAtUtc) <= now.AddDays(-90)) return null;
        var task = await (from current in db.Tasks.AsNoTracking()
            join project in db.Projects.AsNoTracking() on current.ProjectId equals project.Id
            join recipient in db.Users.AsNoTracking() on current.PerformerId equals recipient.Id
            where current.Id == payload.TaskId && current.ProjectId == payload.ProjectId && current.PerformerId == payload.PerformerId
                && recipient.TeamId == project.TeamId
            select new { current.Name, ProjectName = project.Name, current.CreatedAt, current.State, current.DueDate,
                current.ReminderEnabled, current.ReminderOffsetMinutes, current.ReminderNotificationId,
                current.EscalationEnabled, current.EscalationDelayMinutes, current.EscalationNotificationId }).SingleOrDefaultAsync(ct);
        if (task is null || string.IsNullOrWhiteSpace(task.Name) || task.Name.Length > 100
            || string.IsNullOrWhiteSpace(task.ProjectName) || task.ProjectName.Length > 100) return null;
        switch (payload.Type)
        {
            case NotificationEventType.TaskCreated:
                if (NotificationPayloadValidator.AsUtc(task.CreatedAt) != payload.Envelope.TaskCreated!.CreatedAtUtc) return null;
                break;
            case NotificationEventType.ExecutedTaskCompleted:
                var execution = await db.ExecutedTasks.AsNoTracking().Where(value => value.TaskId == payload.TaskId
                    && value.NotificationMessageId == payload.MessageId).Select(value => (DateTime?)value.CreatedAt).SingleOrDefaultAsync(ct);
                if (task.State != TaskState.Done || execution is null
                    || NotificationPayloadValidator.AsUtc(execution.Value) != payload.Envelope.ExecutedTask!.CompletedAtUtc) return null;
                break;
            case NotificationEventType.TaskReminder:
            case NotificationEventType.TaskOverdueEscalation:
                var reminder = payload.Envelope.Reminder!;
                if (task.State is not (TaskState.ToDo or TaskState.InProgress) || task.DueDate is null
                    || NotificationPayloadValidator.AsUtc(task.DueDate.Value) != reminder.DueDateUtc
                    || task.ReminderOffsetMinutes is { } offset && offset != reminder.ReminderOffsetMinutes
                    || task.EscalationDelayMinutes is { } delay && delay != reminder.EscalationDelayMinutes) return null;
                if (payload.Type == NotificationEventType.TaskReminder && (!task.ReminderEnabled || task.ReminderNotificationId != payload.MessageId)) return null;
                if (payload.Type == NotificationEventType.TaskOverdueEscalation && (!task.EscalationEnabled || task.EscalationNotificationId != payload.MessageId)) return null;
                break;
        }
        return new(task.Name, task.ProjectName, NotificationPayloadValidator.AsUtc(intent.CreatedAtUtc));
    }

    private static bool MatchesKind(NotificationOutboxMessage intent, NotificationEventType type) => intent.Kind switch
    {
        NotificationOutboxKind.TaskCreated => type == NotificationEventType.TaskCreated && !intent.IsEscalation,
        NotificationOutboxKind.ExecutedTaskCompleted => type == NotificationEventType.ExecutedTaskCompleted && !intent.IsEscalation,
        NotificationOutboxKind.Reminder => type == (intent.IsEscalation ? NotificationEventType.TaskOverdueEscalation : NotificationEventType.TaskReminder),
        _ => false
    };

    private sealed record Candidate(string TaskName, string ProjectName, DateTime OccurredAtUtc);
}
