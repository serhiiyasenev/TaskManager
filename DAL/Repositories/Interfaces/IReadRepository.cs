using System.Linq.Expressions;

namespace DAL.Repositories.Interfaces;

public interface IReadRepository<T>
{
    IQueryable<T> Query();
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);
    Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

    Task<List<TOut>> SelectAsync<TOut>(
        Expression<Func<T, bool>>? filter,
        Expression<Func<T, TOut>> selector,
        Func<IQueryable<T>, IQueryable<T>>? orderBy = null,
        int? skip = null, int? take = null,
        CancellationToken ct = default);
}