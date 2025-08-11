using BLL.Interfaces;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController(IProjectsService projectsService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<Project>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<Project>>> GetAll()
    {
        return Ok(await projectsService.GetProjectsAsync());
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Project), StatusCodes.Status200OK)]
    public async Task<ActionResult<Project>> GetById([FromRoute] int id)
    {
        return Ok(await projectsService.GetProjectByIdAsync(id));
    }

    [HttpPost]
    [ProducesResponseType(typeof(Project), StatusCodes.Status200OK)]
    public async Task<ActionResult<Project>> Add([FromBody] Project project)
    {
        return Ok(await projectsService.AddProjectAsync(project));
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(Project), StatusCodes.Status200OK)]
    public async Task<ActionResult<Project>> Update([FromRoute] int id, [FromBody] Project project)
    {
        return Ok(await projectsService.UpdateProjectByIdAsync(id, project));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Project>> DeleteById([FromRoute] int id)
    {
        await projectsService.DeleteProjectByIdAsync(id);
        return NoContent();
    }
}