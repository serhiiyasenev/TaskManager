using DAL.Context;
using DAL.Repositories.Interfaces;

namespace DAL.Repositories.Implementation;

internal sealed class UnitOfWork(TaskContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}