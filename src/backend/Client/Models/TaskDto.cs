using System.Globalization;

namespace Client.Models
{
    public record TaskDto(
        int Id,
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
        DateTime? EscalationSentAt)
    {
        public override string ToString()
        {
            var finishedAtStr = FinishedAt.HasValue ? FinishedAt.Value.ToString(CultureInfo.InvariantCulture) : "N/A";
            var dueStr = DueDate.HasValue ? DueDate.Value.ToString(CultureInfo.InvariantCulture) : "N/A";
            var reminder = ReminderEnabled
                ? $"ON (offset {ReminderOffsetMinutes ?? 0}m, sent {(ReminderSentAt.HasValue ? ReminderSentAt.Value.ToString(CultureInfo.InvariantCulture) : "no")})"
                : "OFF";
            var escalation = EscalationEnabled
                ? $"ON (delay {EscalationDelayMinutes ?? 0}m, sent {(EscalationSentAt.HasValue ? EscalationSentAt.Value.ToString(CultureInfo.InvariantCulture) : "no")})"
                : "OFF";
            return $"TaskId: {Id}\nTaskName: {Name}\nDescription: {Description}\nState: {State}\nCreatedAt: {CreatedAt}\nFinishedAt: {finishedAtStr}\nDueDate: {dueStr}\nReminder: {reminder}\nEscalation: {escalation}";
        }
    }
}
