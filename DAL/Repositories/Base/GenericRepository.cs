using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DAL.Repositories.Base;

internal sealed class GenericRepository<TEntity> : IGenericRepository<TEntity> where TEntity : class
{
    private readonly DbContext _ctx;
    private readonly DbSet<TEntity> _set;

    public GenericRepository(DbContext ctx)
    {
        _ctx = ctx;
        _set = _ctx.Set<TEntity>();
    }

    public async Task<List<TEntity>> GetAllAsync() =>
        await _set.AsNoTracking().ToListAsync();

    public async Task<TEntity?> GetByIdAsync(int id) =>
        await _set.FindAsync(id);

    public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate) =>
        await _set.AsNoTracking().AnyAsync(predicate);

    public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<TEntity, bool>>? predicate = null) =>
        predicate is null
            ? await _set.AsNoTracking().CountAsync()
            : await _set.AsNoTracking().CountAsync(predicate);

    public async Task<TEntity> AddAsync(TEntity entity)
    {
        _ = await _set.AddAsync(entity);
        await _ctx.SaveChangesAsync();
        return entity;
    }

    public async Task<TEntity> UpdateAsync(TEntity entity)
    {
        _set.Update(entity);
        await _ctx.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _set.FindAsync(id);
        if (entity is null) return false;
        _set.Remove(entity);
        await _ctx.SaveChangesAsync();
        return true;
    }
}