using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Models.Users;
using DAL.Entities;
using DAL.Repositories.Interfaces;
using Task = System.Threading.Tasks.Task;

namespace BLL.Services;

public class UsersService(IRepository<User> users, IReadRepository<Team> teams, IUnitOfWork uow) : IUsersService
{
    public async Task<List<UserDto>> GetUsersAsync()
    {
        var entities = await users.ListAsync();
        return entities.Select(u => new UserDto(u.Id, u.TeamId, u.FirstName, u.LastName, u.Email, u.RegisteredAt, u.BirthDay)).ToList();
    }

    public async Task<UserDto> GetUserByIdAsync(int id)
    {
        var user = await users.GetByIdAsync(id) ?? throw new NotFoundException(nameof(User), id);
        return new UserDto(user.Id, user.TeamId, user.FirstName, user.LastName, user.Email, user.RegisteredAt, user.BirthDay);
    }

    public async Task<UserDto> UpdateUserByIdAsync(int id, UpdateUserDto user)
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
        return new UserDto(entity.Id, entity.TeamId, entity.FirstName, entity.LastName, entity.Email, entity.RegisteredAt, entity.BirthDay);
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