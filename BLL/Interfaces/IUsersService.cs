using BLL.Models.Users;
using Task = System.Threading.Tasks.Task;

namespace BLL.Interfaces;

public interface IUsersService
{
    Task<List<UserDto>> GetUsersAsync();
    Task<UserDto> GetUserByIdAsync(int id);
    Task<UserDto> UpdateUserByIdAsync(int id, UpdateUserDto user);
    Task DeleteUserByIdAsync(int id);
}
