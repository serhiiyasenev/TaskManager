using DAL.Entities.Base;

namespace DAL.Entities;

public class ExecutedTask : BaseEntity
{
    public int TaskId { get; set; }
    public string TaskName { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    // Internal durable intent reference; preserve the legacy public DTO.
    [System.Text.Json.Serialization.JsonIgnore]
    public Guid? NotificationMessageId { get; set; }
}
