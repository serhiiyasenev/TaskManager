using BLL.Common;
using BLL.Models.Users;

namespace BLL.Interfaces;

public interface IUsersService
{
    Task<Result<List<UserDto>>> GetUsersAsync(CancellationToken ct = default);
    Task<Result<UserDto>> GetUserByIdAsync(int id, CancellationToken ct = default);
    Task<Result<UserDto>> UpdateUserByIdAsync(int id, UpdateUserDto user, CancellationToken ct = default);
    Task<Result> DeleteUserByIdAsync(int id, CancellationToken ct = default);
}
