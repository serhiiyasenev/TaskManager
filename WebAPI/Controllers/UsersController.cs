using BLL.Interfaces;
using BLL.Models.Users;
using DAL.Entities;
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
    public async Task<ActionResult<List<UserDto>>> GetAll()
    {
        return Ok(await usersService.GetUsersAsync());
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Get user by Id")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> GetById([FromRoute] int id)
    {
        return Ok(await usersService.GetUserByIdAsync(id));
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Update user by Id")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> Update([FromRoute] int id, [FromBody] UpdateUserDto user)
    {
        return Ok(await usersService.UpdateUserByIdAsync(id, user));
    }

    [Authorize]
    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Delete user by Id", Description = "Only a authenticated user can delete users.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [SwaggerResponse(401, "Invalid credentials")]
    public async Task<ActionResult<Team>> DeleteById([FromRoute] int id)
    {
        await usersService.DeleteUserByIdAsync(id);
        return NoContent();
    }
}