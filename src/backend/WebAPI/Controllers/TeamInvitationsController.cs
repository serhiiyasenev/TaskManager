using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Teams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/team-invitations")]
public sealed class TeamInvitationsController(ITeamManagementService management) : ControllerBase
{
    [HttpGet("preview")]
    [AllowAnonymous]
    [EnableRateLimiting("account-authentication")]
    public async Task<ActionResult<TeamInvitationPreview>> Preview([FromQuery] string token, CancellationToken ct) =>
        (await management.PreviewInvitationAsync(token, ct)).ToActionResult();

    [HttpPost("accept")]
    [EnableRateLimiting("account-authentication")]
    public async Task<ActionResult<AcceptedTeamInvitation>> Accept([FromBody] AcceptTeamInvitationRequest request, CancellationToken ct)
    {
        var result = await management.AcceptInvitationAsync(request.Token, ct);
        if (result.IsFailure && result.Error.Code == "Error.Gone")
            return StatusCode(StatusCodes.Status410Gone, new ProblemDetails
            {
                Status = StatusCodes.Status410Gone, Title = "Invitation no longer available.", Detail = result.Error.Message
            });
        return result.ToActionResult();
    }
}
