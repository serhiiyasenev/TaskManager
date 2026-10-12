#nullable enable
namespace DAL.Entities;

public sealed class UserNotification
{
    public Guid Id { get; set; }
    public int OwnerId { get; set; }
    public int Kind { get; set; }
    public int TaskId { get; set; }
    public int ProjectId { get; set; }
    public string TaskName { get; set; } = null!;
    public string ProjectName { get; set; } = null!;
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public User Owner { get; set; } = null!;
}
