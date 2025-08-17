using DAL.Context;
using DAL.Repositories.Interfaces;

namespace DAL.Repositories.Implementation;

internal sealed class UnitOfWork(TaskContext db) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken ct = default) => await db.SaveChangesAsync(ct);
}