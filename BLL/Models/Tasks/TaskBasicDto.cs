namespace BLL.Models.Tasks;

public record TaskBasicDto(
    int Id,
    int ProjectId,
    int PerformerId,
    string Name,
    string Description,
    string State,
    DateTime CreatedAt,
    DateTime? FinishedAt,
    DateTime? DueDate,
    bool ReminderEnabled,
    int? ReminderOffsetMinutes,
    bool EscalationEnabled,
    int? EscalationDelayMinutes,
    DateTime? ReminderSentAt,
    DateTime? EscalationSentAt);
