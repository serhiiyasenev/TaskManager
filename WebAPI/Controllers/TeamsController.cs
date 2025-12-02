using BLL.Common;
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
    [ProducesResponseType(typeof(List<TeamDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<TeamDetailDto>>> GetAll(CancellationToken ct)
    {
        var result = await teamsService.GetTeamsAsync(ct);
        return result.ToActionResult();
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(TeamDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TeamDetailDto>> GetById([FromRoute] int id, CancellationToken ct)
    {
        var result = await teamsService.GetTeamByIdAsync(id, ct);
        return result.ToActionResult();
    }

    [HttpPost]
    [ProducesResponseType(typeof(Team), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Team>> Add([FromBody] CreateTeamDto team, CancellationToken ct)
    {
        var result = await teamsService.AddTeamAsync(team, ct);
        return result.ToActionResult();
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(Team), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Team>> Update([FromRoute] int id, [FromBody] UpdateTeamDto team, CancellationToken ct)
    {
        var result = await teamsService.UpdateTeamByIdAsync(id, team, ct);
        return result.ToActionResult();
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteById([FromRoute] int id, CancellationToken ct)
    {
        var result = await teamsService.DeleteTeamByIdAsync(id, ct);
        return result.ToActionResult();
    }
}