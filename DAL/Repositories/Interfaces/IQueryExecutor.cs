namespace DAL.Repositories.Interfaces
{
    public interface IQueryExecutor
    {
        Task<List<T>> ToListAsync<T>(IQueryable<T> queryable, CancellationToken ct = default);
        Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> queryable, CancellationToken ct = default);
        Task<int> CountAsync<T>(IQueryable<T> queryable, CancellationToken ct = default);
        Task<bool> AnyAsync<T>(IQueryable<T> queryable, CancellationToken ct = default);
    }
}
