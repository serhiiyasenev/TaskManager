using System.Linq.Expressions;

namespace DAL.Repositories.Interfaces;

public interface IReadRepository<T>
{
    IQueryable<T> Query();
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);
    Task<T?> GetByIdWithIncludesAsync(int id, CancellationToken ct = default, params Expression<Func<T, object>>[] includes);
    Task<List<T>> ListWithIncludesAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default, params Expression<Func<T, object>>[] includes);
}