namespace Client.Models;

public class ExecutedTaskDto
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string TaskName { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}