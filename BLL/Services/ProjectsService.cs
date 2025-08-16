using BLL.Exceptions;
using BLL.Interfaces;
using DAL.Entities;
using DAL.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace BLL.Services;

public class ProjectsService(
    IRepository<Project> projects,
    IReadRepository<User> users,
    IReadRepository<Team> teams,
    IUnitOfWork uow, ILogger<ProjectsService> logger)
    : IProjectsService
{
    public async Task<List<Project>> GetProjectsAsync() => await projects.ListAsync();

    public async Task<Project> GetProjectByIdAsync(int id)
    {
        logger.LogInformation($"Start work on project {id}");
        return await projects.GetByIdAsync(id) ?? throw new NotFoundException(nameof(Project), id);
    }

    public async Task<Project> AddProjectAsync(Project project)
    {
        if (!await users.AnyAsync(u => u.Id == project.AuthorId)) throw new NotFoundException("AuthorId", project.AuthorId);
        if (!await teams.AnyAsync(t => t.Id == project.TeamId)) throw new NotFoundException("TeamId", project.TeamId);

        project.Id = 0;
        project.CreatedAt = DateTime.UtcNow;

        await projects.AddAsync(project);
        await uow.SaveChangesAsync();
        return project;
    }

    public async Task<Project> UpdateProjectByIdAsync(int id, Project project)
    {
        var entity = await projects.GetByIdAsync(id) ?? throw new NotFoundException(nameof(Project), id);
        if (!await users.AnyAsync(u => u.Id == project.AuthorId)) throw new NotFoundException("AuthorId", project.AuthorId);
        if (!await teams.AnyAsync(t => t.Id == project.TeamId)) throw new NotFoundException("TeamId", project.TeamId);

        entity.AuthorId = project.AuthorId;
        entity.TeamId = project.TeamId;
        entity.Name = project.Name;
        entity.Description = project.Description;
        entity.Deadline = project.Deadline;

        projects.Update(entity);
        await uow.SaveChangesAsync();
        return entity;
    }

    public async Task DeleteProjectByIdAsync(int id)
    {
        var entity = await projects.GetByIdAsync(id) ?? throw new NotFoundException(nameof(Project), id);
        projects.Remove(entity);
        await uow.SaveChangesAsync();
    }
}
