using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(IUsersService usersService, IAuthService authService) : ControllerBase
{
    [HttpPost("Register")]
    [SwaggerOperation(Summary = "Register user", Description = "Registers a new user account.")]
    [SwaggerResponse(200, "User successfully registered", typeof(string))]
    [SwaggerResponse(400, "Validation or registration error")]
    public async Task<IActionResult> Register([FromBody] RegisterUserDto model)
    {
        var result = await authService.RegisterAsync(model);
        return Ok(result);
    }

    [HttpPost("Login")]
    [SwaggerOperation(Summary = "User login", Description = "Authenticates the user and returns access token.")]
    [SwaggerResponse(200, "Login successful", typeof(UserLoginInfoDto))]
    [SwaggerResponse(401, "Invalid credentials")]
    [SwaggerResponse(403, "Forbidden")]
    public async Task<IActionResult> Login([FromBody] LoginUserDto model)
    {
        var result = await authService.LoginAsync(model);
        return Ok(result);
    }

    [HttpGet]
    [Authorize(Roles = "admin")]
    [SwaggerOperation(Summary = "Get all users", Description = "Only a user who is authenticated as an **admin** can view the entire list of users.")]
    [ProducesResponseType(typeof(List<UserDto>), StatusCodes.Status200OK)]
    [SwaggerResponse(401, "Invalid credentials")]
    [SwaggerResponse(403, "Forbidden")]
    [SwaggerResponse(500, "Internal server error")]
    public async Task<ActionResult<List<UserDto>>> GetAll(CancellationToken ct)
    {
        var result = await usersService.GetUsersAsync(ct);
        return result.ToActionResult();
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Get user by Id")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UserDto>> GetById([FromRoute] int id, CancellationToken ct)
    {
        var result = await usersService.GetUserByIdAsync(id, ct);
        return result.ToActionResult();
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Update user by Id")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UserDto>> Update([FromRoute] int id, [FromBody] UpdateUserDto user, CancellationToken ct)
    {
        var result = await usersService.UpdateUserByIdAsync(id, user, ct);
        return result.ToActionResult();
    }

    [Authorize]
    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Delete user by Id", Description = "Only a authenticated user can delete users.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [SwaggerResponse(401, "Invalid credentials")]
    [SwaggerResponse(500, "Internal server error")]
    public async Task<ActionResult> DeleteById([FromRoute] int id, CancellationToken ct)
    {
        var result = await usersService.DeleteUserByIdAsync(id, ct);
        return result.ToActionResult();
    }
}