using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Projects;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Controllers;

[ApiController]
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
    [ProducesResponseType(typeof(Project), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Project>> Add([FromBody] Project project, CancellationToken ct)
    {
        var result = await projectsService.AddProjectAsync(project, ct);
        return result.ToActionResult();
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(Project), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Project>> Update([FromRoute] int id, [FromBody] Project project, CancellationToken ct)
    {
        var result = await projectsService.UpdateProjectByIdAsync(id, project, ct);
        return result.ToActionResult();
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