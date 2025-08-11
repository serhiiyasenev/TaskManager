using BLL.Exceptions;
using BLL.Interfaces;
using DAL.Entities;
using Task = System.Threading.Tasks.Task;

namespace BLL.Services;

public class UsersService(IDataProvider dataProvider) : IUsersService
{
    public async Task<List<User>> GetUsersAsync()
    {
        return await dataProvider.GetUsersAsync();
    }

    public async Task<User> GetUserByIdAsync(int id)
    {
        return await dataProvider.GetUserByIdAsync(id) ?? throw new NotFoundException(nameof(User), id);
    }

    public async Task<User> AddUserAsync(User user)
    {
        return await dataProvider.AddUserAsync(user);
    }

    public async Task<User> UpdateUserByIdAsync(int id, User user)
    {
        return await dataProvider.UpdateUserByIdAsync(id, user) ?? throw new NotFoundException(nameof(User), id);
    }

    public async Task DeleteUserByIdAsync(int id)
    {
        var isDeleted = await dataProvider.DeleteUserByIdAsync(id) ?? throw new NotFoundException(nameof(User), id);
        if (isDeleted == false) throw new CanNotDeleteException(nameof(User), id);
    }
}