namespace DAL.Entities;

public enum NotificationProcessingDisposition { Delivered = 0, Suppressed = 1 }

public sealed class NotificationProcessingMarker
{
    public Guid MessageId { get; set; }
    public string CanonicalPayloadHash { get; set; } = null!;
    public DateTime ProcessedAtUtc { get; set; }
    public NotificationProcessingDisposition Disposition { get; set; }
}
