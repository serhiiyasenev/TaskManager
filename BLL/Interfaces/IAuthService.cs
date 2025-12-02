using BLL.Models.Users;

namespace BLL.Interfaces;

public interface IAuthService
{
    Task<string> RegisterAsync(RegisterUserDto model, CancellationToken ct = default);
    Task<UserLoginInfoDto> LoginAsync(LoginUserDto model, CancellationToken ct = default);
}