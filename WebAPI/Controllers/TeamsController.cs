using BLL.Interfaces;
using BLL.Models.Teams;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TeamsController(ITeamsService teamsService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<Team>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<Team>>> GetAll()
    {
        return Ok(await teamsService.GetTeamsAsync());
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Team), StatusCodes.Status200OK)]
    public async Task<ActionResult<Team>> GetById([FromRoute] int id)
    {
        return Ok(await teamsService.GetTeamByIdAsync(id));
    }

    [HttpPost]
    [ProducesResponseType(typeof(Team), StatusCodes.Status200OK)]
    public async Task<ActionResult<Team>> Add([FromBody] CreateTeamDto team)
    {
        return Ok(await teamsService.AddTeamAsync(team));
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(Team), StatusCodes.Status200OK)]
    public async Task<ActionResult<Team>> Update([FromRoute] int id, [FromBody] UpdateTeamDto team)
    {
        return Ok(await teamsService.UpdateTeamByIdAsync(id, team));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<Team>> DeleteById([FromRoute] int id)
    {
        await teamsService.DeleteTeamByIdAsync(id);
        return NoContent();
    }
}