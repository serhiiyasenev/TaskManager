#nullable enable
namespace DAL.Entities;

public sealed class AiTaskMaterialization
{
    public Guid DraftItemId { get; set; }
    public int OriginalTaskId { get; set; }
    public int? LiveTaskId { get; set; }
    public Guid OutboxMessageId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public AiTaskDraftItem DraftItem { get; set; } = null!;
    public Task? LiveTask { get; set; }
}
