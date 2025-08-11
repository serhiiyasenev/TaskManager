using DAL.Entities;
using Task = System.Threading.Tasks.Task;

namespace BLL.Interfaces;

public interface IUsersService
{
    Task<List<User>> GetUsersAsync();
    Task<User> GetUserByIdAsync(int id);
    Task<User> AddUserAsync(User user);
    Task<User> UpdateUserByIdAsync(int id, User user);
    Task DeleteUserByIdAsync(int id);
}
