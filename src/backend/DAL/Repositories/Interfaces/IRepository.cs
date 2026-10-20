namespace DAL.Repositories.Interfaces
{
    public interface IRepository<T> : IReadRepository<T>, IWriteRepository<T>;

}
