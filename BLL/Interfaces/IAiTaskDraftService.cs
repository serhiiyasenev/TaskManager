using BLL.Common;
using BLL.Models.AI;

namespace BLL.Interfaces;

public interface IAiTaskDraftService
{
    AiProviderStatusDto GetStatus();
    Task<Result<AiTaskDraftDto>> ExtractAsync(CreateAiTaskDraftRequest request, CancellationToken ct = default);
    Task<Result<AiTaskDraftDto>> GetAsync(Guid draftId, CancellationToken ct = default);
    Task<Result<AiTaskCommitResponse>> CommitAsync(Guid draftId, AiTaskCommitRequest request, string idempotencyKey, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid draftId, CancellationToken ct = default);
    Task<int> CleanupExpiredAsync(CancellationToken ct = default);
}
