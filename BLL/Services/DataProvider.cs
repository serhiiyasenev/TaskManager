using BLL.Exceptions;
using BLL.Interfaces;
using DAL.Entities;
using DAL.Enum;
using DAL.Repositories.Interfaces;

namespace BLL.Services;

public class DataProvider(
    IGenericRepository<Project> projectsRepo,
    IGenericRepository<DAL.Entities.Task> tasksRepo,
    IGenericRepository<Team> teamsRepo,
    IGenericRepository<User> usersRepo,
    IGenericRepository<ExecutedTask> executedTasksRepo
) : IDataProvider
{
    public async Task<List<Project>> GetProjectsAsync() => await projectsRepo.GetAllAsync();
    public async Task<List<DAL.Entities.Task>> GetTasksAsync() => await tasksRepo.GetAllAsync();
    public async Task<List<Team>> GetTeamsAsync() => await teamsRepo.GetAllAsync();
    public async Task<List<User>> GetUsersAsync() => await usersRepo.GetAllAsync();
    public async Task<Project> GetProjectByIdAsync(int id) => await projectsRepo.GetByIdAsync(id) ?? null;

    public async Task<Project> AddProjectAsync(Project project)
    {
        _ = await usersRepo.GetByIdAsync(project.AuthorId) ?? throw new NotFoundException("AuthorId", project.AuthorId);
        _ = await teamsRepo.GetByIdAsync(project.TeamId) ?? throw new NotFoundException("TeamId", project.TeamId);

        project.Id = 0;
        project.CreatedAt = DateTime.UtcNow;
        return await projectsRepo.AddAsync(project);
    }

    public async Task<Project> UpdateProjectByIdAsync(int id, Project project)
    {
        var entity = await projectsRepo.GetByIdAsync(id) ?? throw new NotFoundException(nameof(Project), id);

        _ = await usersRepo.GetByIdAsync(project.AuthorId) ?? throw new NotFoundException("AuthorId", project.AuthorId);
        _ = await teamsRepo.GetByIdAsync(project.TeamId) ?? throw new NotFoundException("TeamId", project.TeamId);

        entity.AuthorId = project.AuthorId;
        entity.TeamId = project.TeamId;
        entity.Name = project.Name;
        entity.Description = project.Description;
        entity.Deadline = project.Deadline;

        return await projectsRepo.UpdateAsync(entity);
    }

    public async Task<bool?> DeleteProjectByIdAsync(int id)
    {
        try
        {
            var ok = await projectsRepo.DeleteAsync(id);
            return ok;
        }
        catch
        {
            throw new CanNotDeleteException(nameof(Project), id);
        }
    }

    public async Task<DAL.Entities.Task> GetTaskByIdAsync(int id) => await tasksRepo.GetByIdAsync(id);

    public async Task<DAL.Entities.Task> AddTaskAsync(DAL.Entities.Task task)
    {
        _ = await usersRepo.GetByIdAsync(task.PerformerId) ?? throw new NotFoundException("PerformerId", task.PerformerId);
        _ = await projectsRepo.GetByIdAsync(task.ProjectId) ?? throw new NotFoundException("ProjectId", task.ProjectId);

        task.Id = 0;
        task.CreatedAt = DateTime.UtcNow;
        return await tasksRepo.AddAsync(task);
    }

    public async Task<ExecutedTask> AddExecutedTaskAsync(ExecutedTask task)
    {
        task.Id = 0;
        task.CreatedAt = DateTime.UtcNow;
        return await executedTasksRepo.AddAsync(task);
    }

    public async Task<DAL.Entities.Task> UpdateTaskByIdAsync(int id, DAL.Entities.Task task)
    {
        var entity = await tasksRepo.GetByIdAsync(id) ?? throw new NotFoundException(nameof(DAL.Entities.Task), id);

        _ = await usersRepo.GetByIdAsync(task.PerformerId) ?? throw new NotFoundException("PerformerId", task.PerformerId);
        _ = await projectsRepo.GetByIdAsync(task.ProjectId) ?? throw new NotFoundException("ProjectId", task.ProjectId);

        entity.ProjectId = task.ProjectId;
        entity.PerformerId = task.PerformerId;
        entity.Name = task.Name;
        entity.Description = task.Description;
        entity.State = task.State;

        if (task.State is TaskState.Done or TaskState.Canceled)
            entity.FinishedAt ??= DateTime.UtcNow;
        else
            entity.FinishedAt = null;

        return await tasksRepo.UpdateAsync(entity);
    }

    public async Task<bool?> DeleteTaskByIdAsync(int id)
    {
        try
        {
            var ok = await tasksRepo.DeleteAsync(id);
            return ok;
        }
        catch
        {
            throw new CanNotDeleteException(nameof(DAL.Entities.Task), id);
        }
    }

    public async Task<Team> GetTeamByIdAsync(int id) => await teamsRepo.GetByIdAsync(id) ?? null;

    public async Task<Team> AddTeamAsync(Team team)
    {
        team.Id = 0;
        team.CreatedAt = DateTime.UtcNow;
        return await teamsRepo.AddAsync(team);
    }

    public async Task<Team> UpdateTeamByIdAsync(int id, Team team)
    {
        var entity = await teamsRepo.GetByIdAsync(id) ?? throw new NotFoundException(nameof(Team), id);
        entity.Name = team.Name;
        return await teamsRepo.UpdateAsync(entity);
    }

    public async Task<bool?> DeleteTeamByIdAsync(int id)
    {
        try
        {
            var ok = await teamsRepo.DeleteAsync(id);
            return ok;
        }
        catch
        {
            throw new CanNotDeleteException(nameof(Team), id);
        }
    }

    public async Task<User> GetUserByIdAsync(int id) => await usersRepo.GetByIdAsync(id);

    public async Task<User> AddUserAsync(User user)
    {
        if (user.TeamId.HasValue)
            _ = await teamsRepo.GetByIdAsync(user.TeamId.Value) ?? throw new NotFoundException("TeamId", user.TeamId.Value);

        user.Id = 0;
        user.RegisteredAt = DateTime.UtcNow;
        return await usersRepo.AddAsync(user);
    }

    public async Task<User> UpdateUserByIdAsync(int id, User user)
    {
        var entity = await usersRepo.GetByIdAsync(id) ?? throw new NotFoundException(nameof(User), id);

        if (user.TeamId.HasValue)
            _ = await teamsRepo.GetByIdAsync(user.TeamId.Value) ?? throw new NotFoundException("TeamId", user.TeamId.Value);

        entity.TeamId = user.TeamId;
        entity.FirstName = user.FirstName;
        entity.LastName = user.LastName;
        entity.Email = user.Email;
        entity.BirthDay = user.BirthDay;

        return await usersRepo.UpdateAsync(entity);
    }

    public async Task<bool?> DeleteUserByIdAsync(int id)
    {
        try
        {
            var ok = await usersRepo.DeleteAsync(id);
            return ok;
        }
        catch
        {
            throw new CanNotDeleteException(nameof(User), id);
        }
    }
}
