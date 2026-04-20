using BLL.Models.Users;

namespace BLL.Models.Tasks;

public record TaskDetailDto(
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
    DateTime? ReminderSentAt,
    bool EscalationEnabled,
    int? EscalationDelayMinutes,
    DateTime? EscalationSentAt,
    UserBasicDto Performer);
