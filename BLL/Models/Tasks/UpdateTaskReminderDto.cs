namespace BLL.Models.Tasks;

public record UpdateTaskReminderDto(
    DateTime? DueDate,
    bool ReminderEnabled,
    int? ReminderOffsetMinutes,
    bool EscalationEnabled,
    int? EscalationDelayMinutes);
