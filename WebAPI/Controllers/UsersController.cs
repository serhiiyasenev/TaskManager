using BLL.Interfaces;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(IUsersService usersService) : ControllerBase
{
    [HttpGet]
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

    [HttpPost]
    [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
    public async Task<ActionResult<User>> Add([FromBody] User user)
    {
        return Ok(await usersService.AddUserAsync(user));
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
    public async Task<ActionResult<User>> Update([FromRoute] int id, [FromBody] User user)
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