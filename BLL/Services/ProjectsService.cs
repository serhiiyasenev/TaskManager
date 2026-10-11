#nullable enable annotations
using AutoMapper;
using BLL.Authorization;
using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Projects;
using DAL.Entities;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public class ProjectsService(
    IRepository<Project> projects,
    IReadRepository<User> users,
    IReadRepository<Team> teams,
    IUnitOfWork uow,
    IMapper mapper,
    ILogger<ProjectsService> logger,
    IUserAccess? access = null)
    : IProjectsService
{
    public async Task<Result<List<ProjectDetailDto>>> GetProjectsAsync(CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            var projectList = await QueryWithDetails(scope).ToListAsync(ct);
            
            var dtos = mapper.Map<List<ProjectDetailDto>>(projectList);
            logger.LogInformation("Retrieved {Count} projects", dtos.Count);
            return Result<List<ProjectDetailDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving projects");
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<ProjectDetailDto>> GetProjectByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            logger.LogInformation("Retrieving project {ProjectId}", id);
            var project = await QueryWithDetails(scope).FirstOrDefaultAsync(p => p.Id == id, ct);
            
            if (project is null)
            {
                logger.LogWarning("Project {ProjectId} not found", id);
                return Error.NotFound(nameof(Project), id);
            }

            var dto = mapper.Map<ProjectDetailDto>(project);
            return Result<ProjectDetailDto>.Success(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving project {ProjectId}", id);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<Project>> AddProjectAsync(Project project, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            if (scope is not null)
            {
                if (!scope.IsAdmin && scope.TeamId != project.TeamId) return Error.Forbidden;
                project = ProjectWriteRequest.Sanitize(project);
                project.AuthorId = scope.UserId;
            }
            // Validate author exists
            if (!await users.AnyAsync(u => u.Id == project.AuthorId, ct))
            {
                logger.LogWarning("Author {AuthorId} not found", project.AuthorId);
                return Error.NotFound("Author", project.AuthorId);
            }

            // Validate team exists
            if (!await teams.AnyAsync(t => t.Id == project.TeamId, ct))
            {
                logger.LogWarning("Team {TeamId} not found", project.TeamId);
                return Error.NotFound("Team", project.TeamId);
            }

            project.Id = 0;
            project.CreatedAt = DateTime.UtcNow;

            await projects.AddAsync(project, ct);
            await uow.SaveChangesAsync(ct);
            
            logger.LogInformation("Project created: {ProjectId} - {ProjectName}", project.Id, project.Name);
            return Result<Project>.Success(project);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating project {ProjectName}", project.Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<Project>> UpdateProjectByIdAsync(int id, Project project, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            var entity = await projects.GetByIdAsync(id, ct);
            if (entity is null || (scope is { IsAdmin: false } && entity.TeamId != scope.TeamId))
            {
                logger.LogWarning("Project {ProjectId} not found for update", id);
                return Error.NotFound(nameof(Project), id);
            }

            if (scope is { IsAdmin: false } && entity.AuthorId != scope.UserId) return Error.Forbidden;
            if (scope is not null)
            {
                if (project.TeamId != entity.TeamId) return Error.Validation("TeamId", "Project team cannot be changed through this endpoint.");
                project = ProjectWriteRequest.Sanitize(project);
                project.AuthorId = entity.AuthorId;
                project.TeamId = entity.TeamId;
            }

            // Validate author exists
            if (!await users.AnyAsync(u => u.Id == project.AuthorId, ct))
            {
                logger.LogWarning("Author {AuthorId} not found", project.AuthorId);
                return Error.NotFound("Author", project.AuthorId);
            }

            // Validate team exists
            if (!await teams.AnyAsync(t => t.Id == project.TeamId, ct))
            {
                logger.LogWarning("Team {TeamId} not found", project.TeamId);
                return Error.NotFound("Team", project.TeamId);
            }

            entity.AuthorId = project.AuthorId;
            entity.TeamId = project.TeamId;
            entity.Name = project.Name;
            entity.Description = project.Description;
            entity.Deadline = project.Deadline;

            projects.Update(entity);
            await uow.SaveChangesAsync(ct);
            
            logger.LogInformation("Project updated: {ProjectId} - {ProjectName}", id, entity.Name);
            return Result<Project>.Success(entity);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating project {ProjectId}", id);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result> DeleteProjectByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            var entity = await projects.GetByIdAsync(id, ct);
            if (entity is null || (scope is { IsAdmin: false } && entity.TeamId != scope.TeamId))
            {
                logger.LogWarning("Project {ProjectId} not found for deletion", id);
                return Error.NotFound(nameof(Project), id);
            }

            if (scope is { IsAdmin: false } && entity.AuthorId != scope.UserId) return Error.Forbidden;

            projects.Remove(entity);
            await uow.SaveChangesAsync(ct);
            
            logger.LogInformation("Project deleted: {ProjectId}", id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting project {ProjectId}", id);
            return Error.UnexpectedError;
        }
    }
    private IQueryable<Project> QueryWithDetails(UserAccessScope? scope)
    {
        var query = projects.Query().VisibleProjects(scope)
            .Include(project => project.Author).ThenInclude(author => author.Team)
            .Include(project => project.Team).ThenInclude(team => team.Users)
            .Include(project => project.Tasks).ThenInclude(task => task.Performer)
            .AsSplitQuery().AsNoTracking();
        return scope is { IsAdmin: false }
            ? query.Include(project => project.Author).ThenInclude(author => author.Tasks.Where(task => task.Project.TeamId == scope.TeamId))
            : query.Include(project => project.Author).ThenInclude(author => author.Tasks);
    }
}
