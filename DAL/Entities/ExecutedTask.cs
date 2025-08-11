namespace DAL.Entities;

public class ExecutedTask : BaseEntity
{
    public int TaskId { get; set; }
    public string TaskName { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
