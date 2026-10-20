using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Teams;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TeamsController(ITeamsService teamsService, ITeamManagementService? management = null) : ControllerBase
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
    [ProducesResponseType(typeof(TeamBasicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Team>> Add([FromBody] CreateTeamDto team, CancellationToken ct)
    {
        var result = await teamsService.AddTeamAsync(team, ct);
        return result.IsFailure ? result.ToActionResult() : Ok(new TeamBasicDto(result.Value!.Id, result.Value.Name, result.Value.CreatedAt, result.Value.OwnerId));
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(TeamBasicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Team>> Update([FromRoute] int id, [FromBody] UpdateTeamDto team, CancellationToken ct)
    {
        var result = await teamsService.UpdateTeamByIdAsync(id, team, ct);
        return result.IsFailure ? result.ToActionResult() : Ok(new TeamBasicDto(result.Value!.Id, result.Value.Name, result.Value.CreatedAt, result.Value.OwnerId));
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

    [HttpPost("{id:int}/invitations")]
    [EnableRateLimiting("account-authentication")]
    public async Task<ActionResult<CreatedTeamInvitation>> Invite(int id, [FromBody] CreateTeamInvitationRequest request, CancellationToken ct)
    {
        if (management is null) return StatusCode(500);
        var result = await management.CreateInvitationAsync(id, request.Email, ct);
        return result.IsFailure ? result.ToActionResult() : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpGet("{id:int}/invitations")]
    public async Task<ActionResult<List<TeamInvitationDetail>>> Invitations(int id, CancellationToken ct)
    {
        if (management is null) return StatusCode(500);
        return (await management.GetInvitationsAsync(id, ct)).ToActionResult();
    }

    [HttpDelete("{id:int}/invitations/{invitationId:guid}")]
    public async Task<ActionResult> RevokeInvitation(int id, Guid invitationId, CancellationToken ct)
    {
        if (management is null) return StatusCode(500);
        var result = await management.RevokeInvitationAsync(id, invitationId, ct);
        return result.IsFailure ? result.ToActionResult() : NoContent();
    }

    [HttpDelete("{id:int}/members/{userId:int}")]
    public async Task<ActionResult> RemoveMember(int id, int userId, CancellationToken ct)
    {
        if (management is null) return StatusCode(500);
        var result = await management.RemoveMemberAsync(id, userId, ct);
        return result.IsFailure ? result.ToActionResult() : NoContent();
    }

    [HttpPost("leave")]
    public async Task<ActionResult> Leave(CancellationToken ct)
    {
        if (management is null) return StatusCode(500);
        var result = await management.LeaveTeamAsync(ct);
        return result.IsFailure ? result.ToActionResult() : NoContent();
    }
}
