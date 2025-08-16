using BLL.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Go.WebApi.Controllers;

[Route("api/Auth")]
[ApiController]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("Register")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Register user", Description = "Registers a new user account.")]
    [SwaggerResponse(200, "User successfully registered", typeof(string))]
    [SwaggerResponse(400, "Validation or registration error")]
    public async Task<IActionResult> Register([FromBody] RegisterUserDto model)
    {
        var result = await authService.RegisterAsync(model);
        return Ok(result);
    }

    [HttpPost("Login")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "User login", Description = "Authenticates the user and returns access token.")]
    [SwaggerResponse(200, "Login successful", typeof(UserLoginInfoDto))]
    [SwaggerResponse(401, "Invalid credentials")]
    [SwaggerResponse(403, "Forbidden")]
    public async Task<IActionResult> Login([FromBody] LoginUserDto model)
    {
        var result = await authService.LoginAsync(model);
        return Ok(result);
    }

    [HttpGet("Me/{id}")]
    [Authorize]
    [SwaggerOperation(Summary = "Get current user info", Description = "Returns full user profile for authenticated user.")]
    [SwaggerResponse(200, "User info retrieved", typeof(FullUserDto))]
    [SwaggerResponse(401, "Unauthorized access")]
    [SwaggerResponse(404, "User not found")]
    public async Task<IActionResult> GetCurrentUserInfo(Guid id)
    {
        var result = await authService.GetFullUserInfoAsync(id);
        return Ok(result);
    }

    [HttpGet("GetById/{id}")]
    [Authorize(Roles = "admin")]
    [SwaggerOperation(Summary = "Get public user info", Description = "Returns public user info by user ID.")]
    [SwaggerResponse(200, "Public user info retrieved", typeof(FullUserDto))]
    [SwaggerResponse(404, "User not found")]
    public async Task<IActionResult> GetPublicUserInfo(Guid id)
    {
        var result = await authService.GetPublicUserInfoAsync(id);
        return Ok(result);
    }
}