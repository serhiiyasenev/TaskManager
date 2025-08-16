using BLL.Exceptions;
using BLL.Interfaces;
using DAL.Entities;
using DAL.Repositories.Interfaces;
using Task = System.Threading.Tasks.Task;

namespace BLL.Services;

public class UsersService(IRepository<User> users, IReadRepository<Team> teams, IUnitOfWork uow) : IUsersService
{
    public async Task<List<User>> GetUsersAsync() => await users.ListAsync();

    public async Task<User> GetUserByIdAsync(int id) => await users.GetByIdAsync(id) ?? throw new NotFoundException(nameof(User), id);

    public async Task<User> AddUserAsync(User user)
    {
        if (user.TeamId.HasValue && !await teams.AnyAsync(t => t.Id == user.TeamId.Value))
            throw new NotFoundException("TeamId", user.TeamId.Value);

        user.Id = 0;
        user.RegisteredAt = DateTime.UtcNow;
        await users.AddAsync(user);
        await uow.SaveChangesAsync();
        return user;
    }

    public async Task<User> UpdateUserByIdAsync(int id, User user)
    {
        var entity = await users.GetByIdAsync(id) ?? throw new NotFoundException(nameof(User), id);

        if (user.TeamId.HasValue && !await teams.AnyAsync(t => t.Id == user.TeamId.Value))
            throw new NotFoundException("TeamId", user.TeamId.Value);

        entity.TeamId = user.TeamId;
        entity.FirstName = user.FirstName;
        entity.LastName = user.LastName;
        entity.Email = user.Email;
        entity.BirthDay = user.BirthDay;

        users.Update(entity);
        await uow.SaveChangesAsync();
        return entity;
    }

    public async Task DeleteUserByIdAsync(int id)
    {
        var entity = await users.GetByIdAsync(id) ?? throw new NotFoundException(nameof(User), id);
        try
        {
            users.Remove(entity);
            await uow.SaveChangesAsync();
        }
        catch
        {
            throw new CanNotDeleteException(nameof(User), id);
        }
    }
}