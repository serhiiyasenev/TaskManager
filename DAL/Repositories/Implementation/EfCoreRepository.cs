using System.Linq.Expressions;
using DAL.Context;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DAL.Repositories.Implementation;

internal sealed class EfCoreRepository<T>(TaskContext db) : IRepository<T> where T : class
{
    private DbSet<T> Set => db.Set<T>();

    public IQueryable<T> Query() => Set.AsNoTracking();

    public async Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
        => await Set.FindAsync([id], ct);

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => Set.AsNoTracking().AnyAsync(predicate, ct);

    public Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
        => (predicate is null ? Set.AsNoTracking() : Set.AsNoTracking().Where(predicate)).ToListAsync(ct);

    public async Task AddAsync(T entity, CancellationToken ct = default)
        => await Set.AddAsync(entity, ct);

    public void Update(T entity) => Set.Update(entity);
    public void Remove(T entity) => Set.Remove(entity);
}
