using System.Text.Json.Serialization;

namespace BLL.Models.Messaging;

public enum NotificationEventType
{
    ExecutedTaskCompleted = 0,
    TaskReminder = 1,
    TaskOverdueEscalation = 2
}

public record ExecutedTaskNotification(
    int TaskId,
    string TaskName,
    DateTime CompletedAtUtc);

public record TaskReminderNotification(
    int TaskId,
    string TaskName,
    int PerformerId,
    int ProjectId,
    string? ProjectName,
    DateTime DueDateUtc,
    int? ReminderOffsetMinutes,
    int? EscalationDelayMinutes);

public class TaskNotificationEnvelope
{
    public NotificationEventType Type { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExecutedTaskNotification? ExecutedTask { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TaskReminderNotification? Reminder { get; set; }

    public string? CorrelationId { get; set; }
}
