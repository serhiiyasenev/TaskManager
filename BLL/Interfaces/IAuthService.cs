using BLL.Models.Users;

namespace BLL.Interfaces;

public interface IAuthService
{
    Task<string> RegisterAsync(RegisterUserDto model);
    Task<UserLoginInfoDto> LoginAsync(LoginUserDto model);
}