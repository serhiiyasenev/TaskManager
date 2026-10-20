using System.Linq.Expressions;
using DAL.Context;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DAL.Repositories.Implementation;

public sealed class EfCoreRepository<T>(TaskContext db) : IRepository<T> where T : class
{
    private DbSet<T> Set => db.Set<T>();

    public IQueryable<T> Query() => Set.AsNoTracking();

    public async Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
        => await Set.FindAsync([id], ct);

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => Set.AsNoTracking().AnyAsync(predicate, ct);

    public Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
        => (predicate is null ? Set.AsNoTracking() : Set.AsNoTracking().Where(predicate)).ToListAsync(ct);

    public async Task<T?> GetByIdWithIncludesAsync(int id, CancellationToken ct = default, params Expression<Func<T, object>>[] includes)
    {
        IQueryable<T> query = Set.AsNoTracking();
        
        foreach (var include in includes)
        {
            query = query.Include(include);
        }
        
        return await query.FirstOrDefaultAsync(e => EF.Property<int>(e, "Id") == id, ct);
    }

    public Task<List<T>> ListWithIncludesAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default, params Expression<Func<T, object>>[] includes)
    {
        IQueryable<T> query = Set.AsNoTracking();
        
        foreach (var include in includes)
        {
            query = query.Include(include);
        }
        
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }
        
        return query.ToListAsync(ct);
    }

    public async Task AddAsync(T entity, CancellationToken ct = default)
        => await Set.AddAsync(entity, ct);

    public void Update(T entity) => Set.Update(entity);
    public void Remove(T entity) => Set.Remove(entity);
}
