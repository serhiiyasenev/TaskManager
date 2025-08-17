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

    [HttpGet]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(List<User>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<User>>> GetAll()
    {
        return Ok(await usersService.GetUsersAsync());
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
    public async Task<ActionResult<User>> GetById([FromRoute] int id)
    {
        return Ok(await usersService.GetUserByIdAsync(id));
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
    public async Task<ActionResult<User>> Update([FromRoute] int id, [FromBody] UpdateUserDto user)
    {
        return Ok(await usersService.UpdateUserByIdAsync(id, user));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<Team>> DeleteById([FromRoute] int id)
    {
        await usersService.DeleteUserByIdAsync(id);
        return NoContent();
    }
}