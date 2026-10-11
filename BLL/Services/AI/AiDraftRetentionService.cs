using System.Data;
using BLL.Interfaces;
using BLL.Services.Notifications;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services.AI;

public sealed class AiDraftRetentionService(TaskContext db, TimeProvider clock) : IAiDraftRetentionService
{
    public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Retention requires its own short transaction.");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var now = clock.GetUtcNow().UtcDateTime;
                var drafts = await db.AiTaskDrafts.Include(value => value.Items)
                    .Where(value => value.RetainUntilUtc <= now || value.ExpiresAtUtc <= now
                        && (value.NotesText != null || value.Items.Any(item => item.CandidateJson != null)))
                    .OrderBy(value => value.ExpiresAtUtc).ThenBy(value => value.Id).Take(100).ToListAsync(ct);
                foreach (var draft in drafts)
                {
                    if (draft.RetainUntilUtc <= now) db.AiTaskDrafts.Remove(draft);
                    else
                    {
                        draft.NotesText = null;
                        foreach (var item in draft.Items) item.CandidateJson = null;
                        draft.DeletedAtUtc ??= now;
                        draft.Version = Guid.NewGuid();
                        // RetainUntilUtc was assigned from expiry + receipt TTL
                        // when created; cleanup must never extend that deadline.
                    }
                }
                var receipts = await db.AiTaskCommitReceipts.Where(value => value.ReplayExpiresAtUtc <= now)
                    .OrderBy(value => value.ReplayExpiresAtUtc).ThenBy(value => value.Id).Take(100).ToListAsync(ct);
                db.AiTaskCommitReceipts.RemoveRange(receipts);
                var cutoff = DateOnly.FromDateTime(now).AddDays(-7);
                var ledger = await db.AiDailyUsage.Where(value => value.DayUtc < cutoff)
                    .OrderBy(value => value.DayUtc).ThenBy(value => value.ScopeKey).Take(100).ToListAsync(ct);
                db.AiDailyUsage.RemoveRange(ledger);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return drafts.Count + receipts.Count + ledger.Count;
            }
            catch (Exception exception) when (NotificationSqlErrors.IsDeadlock(exception))
            {
                await NotificationSqlErrors.RollbackAsync(transaction);
                db.ChangeTracker.Clear();
                if (attempt == 1) throw new IOException("AI retention conflicted with another write.");
            }
            catch
            {
                await NotificationSqlErrors.RollbackAsync(transaction);
                db.ChangeTracker.Clear();
                throw;
            }
        }
        throw new IOException("AI retention conflicted with another write.");
    }
}
