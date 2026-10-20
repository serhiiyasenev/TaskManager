using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Projects;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ProjectsController(IProjectsService projectsService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<ProjectDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<ProjectDetailDto>>> GetAll(CancellationToken ct)
    {
        var result = await projectsService.GetProjectsAsync(ct);
        return result.ToActionResult();
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ProjectDetailDto>> GetById([FromRoute] int id, CancellationToken ct)
    {
        var result = await projectsService.GetProjectByIdAsync(id, ct);
        return result.ToActionResult();
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProjectWriteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public Task<ActionResult<Project>> Add([FromBody] ProjectWriteRequest request, CancellationToken ct) => Add(request.ToEntity(), ct);

    [NonAction]
    public async Task<ActionResult<Project>> Add(Project project, CancellationToken ct)
    {
        var result = await projectsService.AddProjectAsync(ProjectWriteRequest.Sanitize(project), ct);
        return result.IsFailure ? result.ToActionResult() : Ok(ProjectWriteResponse.FromEntity(result.Value!));
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ProjectWriteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public Task<ActionResult<Project>> Update([FromRoute] int id, [FromBody] ProjectWriteRequest request, CancellationToken ct) => Update(id, request.ToEntity(), ct);

    [NonAction]
    public async Task<ActionResult<Project>> Update(int id, Project project, CancellationToken ct)
    {
        var result = await projectsService.UpdateProjectByIdAsync(id, ProjectWriteRequest.Sanitize(project), ct);
        return result.IsFailure ? result.ToActionResult() : Ok(ProjectWriteResponse.FromEntity(result.Value!));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteById([FromRoute] int id, CancellationToken ct)
    {
        var result = await projectsService.DeleteProjectByIdAsync(id, ct);
        return result.ToActionResult();
    }
}
