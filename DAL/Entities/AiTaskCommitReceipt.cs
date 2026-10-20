#nullable enable
namespace DAL.Entities;

public sealed class AiTaskCommitReceipt
{
    public Guid Id { get; set; }
    public int OwnerId { get; set; }
    public string KeyHash { get; set; } = null!;
    public string PayloadHash { get; set; } = null!;
    // Historical identifiers deliberately do not have a Draft/Project FK:
    // erasing private notes must not erase a recoverable successful receipt.
    public Guid SourceDraftId { get; set; }
    public int ProjectId { get; set; }
    public Guid ExpectedDraftVersion { get; set; }
    public Guid ResultDraftVersion { get; set; }
    public string ResponseJson { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ReplayExpiresAtUtc { get; set; }
    public User Owner { get; set; } = null!;
}
