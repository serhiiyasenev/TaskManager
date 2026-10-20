using DAL.Context;
using DAL.Repositories.Interfaces;

namespace DAL.Repositories.Implementation;

public sealed class UnitOfWork(TaskContext db) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken ct = default) => await db.SaveChangesAsync(ct);
}