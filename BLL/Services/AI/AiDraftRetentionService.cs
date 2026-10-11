using BLL.Interfaces;
using DAL.Context;

namespace BLL.Services.AI;

// Temporary explicit TDD checkpoint. This service has no inference dependencies.
public sealed class AiDraftRetentionService : IAiDraftRetentionService
{
    public AiDraftRetentionService(TaskContext db, TimeProvider clock) { }
    public Task<int> CleanupExpiredAsync(CancellationToken ct = default) => throw new NotImplementedException();
}
