using System.Linq.Expressions;
using DAL.Context;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DAL.Repositories.Implementation;

internal sealed class EfCoreRepository<T>(TaskContext db) : IRepository<T> where T : class
{
    private DbSet<T> Set => db.Set<T>();

    public IQueryable<T> Query() => Set.AsNoTracking();

    public Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
        => Set.FindAsync([id], ct).AsTask();

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => Set.AsNoTracking().AnyAsync(predicate, ct);

    public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
        => (predicate is null ? Set.AsNoTracking() : Set.AsNoTracking().Where(predicate)).CountAsync(ct);

    public Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
        => (predicate is null ? Set.AsNoTracking() : Set.AsNoTracking().Where(predicate)).ToListAsync(ct);

    public Task<List<TOut>> SelectAsync<TOut>(
        Expression<Func<T, bool>>? filter,
        Expression<Func<T, TOut>> selector,
        Func<IQueryable<T>, IQueryable<T>>? orderBy = null,
        int? skip = null, int? take = null,
        CancellationToken ct = default)
    {
        IQueryable<T> queryable = Set.AsNoTracking();
        if (filter is not null) queryable = queryable.Where(filter);
        if (orderBy is not null) queryable = orderBy(queryable);
        if (skip.HasValue) queryable = queryable.Skip(skip.Value);
        if (take.HasValue) queryable = queryable.Take(take.Value);
        return queryable.Select(selector).ToListAsync(ct);
    }

    // IWrite
    public Task AddAsync(T entity, CancellationToken ct = default)
        => Set.AddAsync(entity, ct).AsTask();

    public void Update(T entity) => Set.Update(entity);

    public void Remove(T entity) => Set.Remove(entity);
}