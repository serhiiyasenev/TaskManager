using BLL.Interfaces.Analytics;
using BLL.Models.Projects;
using BLL.Models.Tasks;
using BLL.Models.Users;
using DAL.Entities;
using DAL.Enum;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services.Analytics;

public class UserAnalyticsService(
    IReadRepository<User> users,
    IReadRepository<Project> projects,
    IReadRepository<DAL.Entities.Task> tasks) : IUserAnalyticsService
{
    public async Task<List<UserWithTasksDto>> GetSortedUsersWithSortedTasksAsync()
    {
        var userRows = await users.Query()
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.RegisteredAt, u.BirthDay })
            .OrderBy(u => u.FirstName)
            .ToListAsync();

        var taskRows = await tasks.Query()
            .Select(t => new { t.Id, t.Name, t.Description, t.State, t.CreatedAt, t.FinishedAt, t.PerformerId })
            .ToListAsync();

        return userRows.Select(u =>
        {
            var userTasks = taskRows
                .Where(t => t.PerformerId == u.Id)
                .OrderByDescending(t => t.Name.Length)
                .Select(t => new TaskDto(
                    t.Id, t.Name, t.Description,
                    t.State == TaskState.ToDo ? "To Do" :
                    t.State == TaskState.InProgress ? "In Progress" :
                    t.State == TaskState.Done ? "Done" : "Canceled",
                    t.CreatedAt, t.FinishedAt))
                .ToList();

            return new UserWithTasksDto(u.Id, u.FirstName, u.LastName, u.Email, u.RegisteredAt, u.BirthDay, userTasks);
        }).ToList();
    }

    public async Task<UserInfoDto> GetUserInfoAsync(int userId)
    {
        var userDto = await users.Query()
            .Where(u => u.Id == userId)
            .Select(u => new UserDto(u.Id, u.TeamId, u.FirstName, u.LastName, u.Email, u.RegisteredAt, u.BirthDay))
            .FirstOrDefaultAsync();
        if (userDto is null) return null;

        var lastProject = await projects.Query()
            .Where(p => p.AuthorId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectDto(p.Id, p.Name, p.Description, p.CreatedAt, p.Deadline))
            .FirstOrDefaultAsync();

        var lastProjectTasksCount = lastProject is null ? 0 : await tasks.Query().CountAsync(t => t.ProjectId == lastProject.Id);

        var notFinishedOrCanceledTasksCount = await tasks.Query()
            .CountAsync(t => t.PerformerId == userId &&
                             (t.State == TaskState.ToDo || t.State == TaskState.InProgress || t.State == TaskState.Canceled));

        var userTasks = await tasks.Query()
            .Where(t => t.PerformerId == userId)
            .Select(t => new { t.Id, t.Name, t.Description, t.State, t.CreatedAt, t.FinishedAt })
            .ToListAsync();

        var longestTask = userTasks.AsQueryable()
            .OrderByDescending(t => (t.FinishedAt ?? DateTime.UtcNow).Ticks - t.CreatedAt.Ticks)
            .Select(t => new TaskDto(
                t.Id, t.Name, t.Description,
                t.State == TaskState.ToDo ? "To Do" :
                t.State == TaskState.InProgress ? "In Progress" :
                t.State == TaskState.Done ? "Done" : "Canceled",
                t.CreatedAt, t.FinishedAt))
            .FirstOrDefault();

        return new UserInfoDto(userDto, lastProject, lastProjectTasksCount, notFinishedOrCanceledTasksCount, longestTask);
    }
}
