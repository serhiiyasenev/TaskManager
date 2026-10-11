using System.ComponentModel.DataAnnotations;
using DAL.Enum;
using TaskEntity = DAL.Entities.Task;

namespace BLL.Models.Tasks;

public sealed class TaskWriteRequest
{
    [Range(1, int.MaxValue)]
    public int ProjectId { get; set; }
    [Range(1, int.MaxValue)]
    public int PerformerId { get; set; }
    [Required, MaxLength(100)]
    public string Name { get; set; } = "";
    [MaxLength(1000)]
    public string Description { get; set; } = "";
    [EnumDataType(typeof(TaskState))]
    public TaskState State { get; set; }
    public DateTime? DueDate { get; set; }
    public bool ReminderEnabled { get; set; }
    [Range(1, 43200)]
    public int? ReminderOffsetMinutes { get; set; }
    public bool EscalationEnabled { get; set; }
    [Range(1, 43200)]
    public int? EscalationDelayMinutes { get; set; }
    public Guid? ExpectedVersion { get; set; }

    public TaskEntity ToEntity() => new()
    {
        ProjectId = ProjectId, PerformerId = PerformerId, Name = Name, Description = Description, State = State,
        DueDate = DueDate, ReminderEnabled = ReminderEnabled, ReminderOffsetMinutes = ReminderOffsetMinutes,
        EscalationEnabled = EscalationEnabled, EscalationDelayMinutes = EscalationDelayMinutes
    };

    public static TaskEntity Sanitize(TaskEntity input) => new()
    {
        ProjectId = input.ProjectId, PerformerId = input.PerformerId, Name = input.Name, Description = input.Description, State = input.State,
        DueDate = input.DueDate, ReminderEnabled = input.ReminderEnabled, ReminderOffsetMinutes = input.ReminderOffsetMinutes,
        EscalationEnabled = input.EscalationEnabled, EscalationDelayMinutes = input.EscalationDelayMinutes
    };
}

public sealed record TaskWriteResponse(int Id, int ProjectId, int PerformerId, string Name, string Description, TaskState State,
    DateTime CreatedAt, DateTime? FinishedAt, DateTime? DueDate, bool ReminderEnabled, int? ReminderOffsetMinutes,
    DateTime? ReminderSentAt, bool EscalationEnabled, int? EscalationDelayMinutes, DateTime? EscalationSentAt, Guid Version = default)
{
    public static TaskWriteResponse FromEntity(TaskEntity task) => new(task.Id, task.ProjectId, task.PerformerId, task.Name, task.Description,
        task.State, task.CreatedAt, task.FinishedAt, task.DueDate, task.ReminderEnabled, task.ReminderOffsetMinutes,
        task.ReminderSentAt, task.EscalationEnabled, task.EscalationDelayMinutes, task.EscalationSentAt, task.Version);
}
