namespace DAL.Entities;

public enum NotificationOutboxStatus { Pending, Published, Canceled }

public class NotificationOutboxMessage
{
    public Guid Id { get; set; }
    public int TaskId { get; set; }
    public bool IsEscalation { get; set; }
    public string QueueName { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public NotificationOutboxStatus Status { get; set; }
    public Guid? LockId { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
}
