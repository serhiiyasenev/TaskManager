#nullable enable
using System.Text.Json.Serialization;

namespace BLL.Models.Messaging;

public enum NotificationEventType
{
    ExecutedTaskCompleted = 0,
    TaskReminder = 1,
    TaskOverdueEscalation = 2,
    TaskCreated = 3
}

public record ExecutedTaskNotification(
    int TaskId,
    string TaskName,
    DateTime CompletedAtUtc,
    int? PerformerId = null,
    int? ProjectId = null,
    string? ProjectName = null);

public record TaskReminderNotification(
    int TaskId,
    string TaskName,
    int PerformerId,
    int ProjectId,
    string? ProjectName,
    DateTime DueDateUtc,
    int? ReminderOffsetMinutes,
    int? EscalationDelayMinutes);

public record TaskCreatedNotification(
    Guid MessageId,
    int TaskId,
    string TaskName,
    int PerformerId,
    int ProjectId,
    string? ProjectName,
    DateTime CreatedAtUtc);

public class TaskNotificationEnvelope
{
    public NotificationEventType Type { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExecutedTaskNotification? ExecutedTask { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TaskReminderNotification? Reminder { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TaskCreatedNotification? TaskCreated { get; set; }

    public string? CorrelationId { get; set; }
}
