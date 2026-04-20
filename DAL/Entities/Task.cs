using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using DAL.Entities.Base;
using DAL.Enum;

namespace DAL.Entities;

[ExcludeFromCodeCoverage]
public class Task : BaseEntity
{
    public int ProjectId { get; set; }
    public int PerformerId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; }
    [MaxLength(1000)]
    public string Description { get; set; }
    public TaskState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime? DueDate { get; set; }
    public bool ReminderEnabled { get; set; }
    public int? ReminderOffsetMinutes { get; set; }
    public DateTime? ReminderSentAt { get; set; }
    public bool EscalationEnabled { get; set; }
    public int? EscalationDelayMinutes { get; set; }
    public DateTime? EscalationSentAt { get; set; }

    public Project Project { get; set; }
    public User Performer { get; set; }


    public Task(
        int id,
        int projectId,
        int performerId,
        string name,
        string description,
        TaskState state,
        DateTime createdAt,
        DateTime? finishedAt,
        Project project,
        User performer,
        DateTime? dueDate,
        bool reminderEnabled,
        int? reminderOffsetMinutes,
        DateTime? reminderSentAt,
        bool escalationEnabled,
        int? escalationDelayMinutes,
        DateTime? escalationSentAt)
    {
        Id = id;
        ProjectId = projectId;
        PerformerId = performerId;
        Name = name;
        Description = description;
        State = state;
        CreatedAt = createdAt;
        FinishedAt = finishedAt;
        DueDate = dueDate;
        ReminderEnabled = reminderEnabled;
        ReminderOffsetMinutes = reminderOffsetMinutes;
        ReminderSentAt = reminderSentAt;
        EscalationEnabled = escalationEnabled;
        EscalationDelayMinutes = escalationDelayMinutes;
        EscalationSentAt = escalationSentAt;
        Project = project;
        Performer = performer;
    }

    public Task()
    {

    }
}
