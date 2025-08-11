using BLL.Exceptions;
using BLL.Interfaces;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Task = DAL.Entities.Task;

namespace BLL.Services;

public class DataProvider(TaskContext context) : IDataProvider
{
    private protected readonly TaskContext Context = context;

    public async Task<List<Project>> GetProjectsAsync()
    {
        return await Context.Projects.AsNoTracking().ToListAsync();
    }

    public async Task<List<Task>> GetTasksAsync()
    {
        return await Context.Tasks.AsNoTracking().ToListAsync();
    }

    public async Task<List<Team>> GetTeamsAsync()
    {
        return await Context.Teams.AsNoTracking().ToListAsync();
    }

    public async Task<List<User>> GetUsersAsync()
    {
        return await Context.Users.AsNoTracking().ToListAsync();
    }

    public async Task<Project> GetProjectByIdAsync(int id)
    {
        return await Context.Projects.FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Project> AddProjectAsync(Project project)
    {
        var author = await Context.Users.FirstOrDefaultAsync(u => u.Id == project.AuthorId) ?? throw new NotFoundException("AuthorId", project.AuthorId);
        var team = await Context.Teams.FirstOrDefaultAsync(u => u.Id == project.TeamId) ?? throw new NotFoundException("TeamId", project.TeamId);

        project.Id = 0;
        project.CreatedAt = DateTime.UtcNow;
        var projectEntity = await Context.Projects.AddAsync(project);
        await Context.SaveChangesAsync();
        return projectEntity.Entity;
    }

    public async Task<Project> UpdateProjectByIdAsync(int id, Project project)
    {
        var projectEntity = await Context.Projects.FirstOrDefaultAsync(p => p.Id == id) ?? throw new NotFoundException(nameof(Project), id);
        var author = await Context.Users.FirstOrDefaultAsync(u => u.Id == project.AuthorId) ?? throw new NotFoundException("AuthorId", project.AuthorId);
        var team = await Context.Teams.FirstOrDefaultAsync(u => u.Id == project.TeamId) ?? throw new NotFoundException("TeamId", project.TeamId);

        projectEntity.AuthorId = project.AuthorId;
        projectEntity.TeamId = project.TeamId;
        projectEntity.Name = project.Name;
        projectEntity.Description = project.Description;
        projectEntity.Deadline = project.Deadline;

        Context.Projects.Update(projectEntity);
        await Context.SaveChangesAsync();

        return projectEntity;
    }

    public async Task<bool?> DeleteProjectByIdAsync(int id)
    {
        try
        {
            var projectEntity = await Context.Projects.FirstOrDefaultAsync(u => u.Id == id) ?? throw new NotFoundException(nameof(Project), id);
            Context.Projects.Remove(projectEntity);
            await Context.SaveChangesAsync();

            return true;
        }
        catch (Exception e)
        {
            throw new CanNotDeleteException(nameof(Project), id);
        }
    }

    public async Task<Task> GetTaskByIdAsync(int id)
    {
        return await Context.Tasks.FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Task> AddTaskAsync(Task task)
    {
        var author = await Context.Users.FirstOrDefaultAsync(u => u.Id == task.PerformerId) ?? throw new NotFoundException("PerformerId", task.PerformerId);
        var project = await Context.Teams.FirstOrDefaultAsync(u => u.Id == task.ProjectId) ?? throw new NotFoundException("ProjectId", task.ProjectId);

        task.Id = 0;
        task.CreatedAt = DateTime.UtcNow;
        var taskEntity = await Context.Tasks.AddAsync(task);
        await Context.SaveChangesAsync();
        return taskEntity.Entity;
    }

    public async Task<ExecutedTask> AddExecutedTaskAsync(ExecutedTask task)
    {
        task.Id = 0;
        task.CreatedAt = DateTime.UtcNow;
        var taskEntity = await Context.ExecutedTasks.AddAsync(task);
        await Context.SaveChangesAsync();
        return taskEntity.Entity;
    }

    public async Task<Task> UpdateTaskByIdAsync(int id, Task task)
    {
        var taskEntity = await Context.Tasks.FirstOrDefaultAsync(p => p.Id == id) ?? throw new NotFoundException(nameof(Task), id);
        var author = await Context.Users.FirstOrDefaultAsync(u => u.Id == task.PerformerId) ?? throw new NotFoundException("PerformerId", task.PerformerId);
        var project = await Context.Teams.FirstOrDefaultAsync(u => u.Id == task.ProjectId) ?? throw new NotFoundException("ProjectId", task.ProjectId);

        taskEntity.ProjectId = task.ProjectId;
        taskEntity.PerformerId = task.PerformerId;
        taskEntity.Name = task.Name;
        taskEntity.Description = task.Description;
        taskEntity.State = task.State;

        if (task.State is TaskState.Done or TaskState.Canceled)
        {
            taskEntity.FinishedAt = DateTime.UtcNow;
        }

        if (task.State is TaskState.ToDo or TaskState.InProgress)
        {
            taskEntity.FinishedAt = null;
        }

        Context.Tasks.Update(taskEntity);
        await Context.SaveChangesAsync();

        return taskEntity;
    }

    public async Task<bool?> DeleteTaskByIdAsync(int id)
    {
        try
        {
            var taskEntity = await Context.Tasks.FirstOrDefaultAsync(u => u.Id == id) ?? throw new NotFoundException(nameof(Task), id);
            Context.Tasks.Remove(taskEntity);
            await Context.SaveChangesAsync();
            return true;
        }
        catch (Exception e)
        {
            throw new CanNotDeleteException(nameof(Task), id);
        }
    }

    public async Task<Team> GetTeamByIdAsync(int id)
    {
        return await Context.Teams.FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Team> AddTeamAsync(Team team)
    {
        team.Id = 0;
        team.CreatedAt = DateTime.UtcNow;
        var teamEntity = await Context.Teams.AddAsync(team);
        await Context.SaveChangesAsync();

        return teamEntity.Entity;
    }

    public async Task<Team> UpdateTeamByIdAsync(int id, Team team)
    {
        var teamEntity = await Context.Teams.FirstOrDefaultAsync(p => p.Id == id) ?? throw new NotFoundException(nameof(Team), id);

        teamEntity.Name = team.Name;
        Context.Teams.Update(teamEntity);
        await Context.SaveChangesAsync();

        return teamEntity;
    }

    public async Task<bool?> DeleteTeamByIdAsync(int id)
    {
        try
        {
            var teamEntity = await Context.Teams.FirstOrDefaultAsync(u => u.Id == id) ?? throw new NotFoundException(nameof(Team), id);
            Context.Teams.Remove(teamEntity);
            await Context.SaveChangesAsync();

            return true;
        }
        catch (Exception)
        {
            throw new CanNotDeleteException(nameof(Team), id);
        }
    }

    public async Task<User> GetUserByIdAsync(int id)
    {
        return await Context.Users.FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<User> AddUserAsync(User user)
    {
        if (user.TeamId != null)
        {
            var team = await Context.Teams.FirstOrDefaultAsync(p => p.Id == user.TeamId) ?? throw new NotFoundException("TeamId", (int)user.TeamId);
        }

        user.Id = 0;
        user.RegisteredAt = DateTime.UtcNow;
        var userEntity = await Context.Users.AddAsync(user);
        await Context.SaveChangesAsync();
        return userEntity.Entity;
    }

    public async Task<User> UpdateUserByIdAsync(int id, User user)
    {
        var userEntity = await Context.Users.FirstOrDefaultAsync(p => p.Id == id) ?? throw new NotFoundException(nameof(User), id);
        if (user.TeamId != null)
        {
            var team = await Context.Teams.FirstOrDefaultAsync(p => p.Id == user.TeamId) ?? throw new NotFoundException("TeamId", (int)user.TeamId);
        }

        userEntity.TeamId = user.TeamId;
        userEntity.FirstName = user.FirstName;
        userEntity.LastName = user.LastName;
        userEntity.Email = user.Email;
        userEntity.BirthDay = user.BirthDay;

        Context.Users.Update(userEntity);
        await Context.SaveChangesAsync();

        return userEntity;
    }

    public async Task<bool?> DeleteUserByIdAsync(int id)
    {
        try
        {
            var userEntity = await Context.Users.FirstOrDefaultAsync(u => u.Id == id) ?? throw new NotFoundException(nameof(User), id);
            Context.Users.Remove(userEntity);
            await Context.SaveChangesAsync();

            return true;
        }
        catch (Exception e)
        {
            throw new CanNotDeleteException(nameof(User), id);
        }
    }
}