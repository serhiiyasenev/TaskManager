using BLL.Models.Users;
using DAL.Entities;
using Task = System.Threading.Tasks.Task;

namespace BLL.Interfaces;

public interface IUsersService
{
    Task<List<User>> GetUsersAsync();
    Task<UserDto> GetUserByIdAsync(int id);
    Task<User> UpdateUserByIdAsync(int id, UpdateUserDto user);
    Task DeleteUserByIdAsync(int id);
}
