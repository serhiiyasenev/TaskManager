namespace DAL.Repositories.Interfaces
{
    public interface IWriteRepository<in T>
    {
        Task AddAsync(T entity, CancellationToken ct = default);
        void Update(T entity);
        void Remove(T entity);
    }
}
