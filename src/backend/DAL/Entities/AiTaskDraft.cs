#nullable enable
namespace DAL.Entities;

public sealed class AiTaskDraft
{
    public Guid Id { get; set; }
    public int OwnerId { get; set; }
    public int ProjectId { get; set; }
    public Guid Version { get; set; }
    public string? NotesText { get; set; }
    public string Locale { get; set; } = null!;
    public string TimeZoneId { get; set; } = null!;
    public DateOnly ReferenceDate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime RetainUntilUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public User Owner { get; set; } = null!;
    public Project Project { get; set; } = null!;
    public List<AiTaskDraftItem> Items { get; set; } = [];
}
