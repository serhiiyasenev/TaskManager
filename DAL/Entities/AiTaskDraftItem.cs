#nullable enable
namespace DAL.Entities;

public sealed class AiTaskDraftItem
{
    public Guid Id { get; set; }
    public Guid DraftId { get; set; }
    public int Ordinal { get; set; }
    // Only validated immutable proposals are kept here. Expiry/deletion erases
    // the JSON immediately while a short-lived identifier tombstone remains.
    public string? CandidateJson { get; set; }
    public AiTaskDraft Draft { get; set; } = null!;
    public AiTaskMaterialization? Materialization { get; set; }
}
