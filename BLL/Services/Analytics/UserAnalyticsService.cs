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
            .Select(u => new UserDto(
                u.Id,
                u.TeamId,
                u.FirstName,
                u.LastName,
                u.Email,
                u.RegisteredAt,
                u.BirthDay
            ))
            .FirstOrDefaultAsync();

        if (userDto == null)
            return null;

        var lastProject = await projects.Query()
            .Where(p => p.AuthorId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectDto(
                p.Id,
                p.Name,
                p.Description,
                p.CreatedAt,
                p.Deadline
            ))
            .FirstOrDefaultAsync();

        var lastProjectTasksCount = lastProject == null
            ? 0
            : await tasks.Query().CountAsync(t => t.ProjectId == lastProject.Id);

        var notFinishedOrCanceledTasksCount = await tasks.Query()
            .CountAsync(t =>
                t.PerformerId == userId &&
                (t.State == TaskState.ToDo ||
                 t.State == TaskState.InProgress ||
                 t.State == TaskState.Canceled)
            );

        // Capture current UTC time outside the query to avoid client evaluation.
        // EF will pass this value as a SQL parameter (@__now_0).
        var now = DateTime.UtcNow;

        // We intentionally use TimeSpan subtraction instead of EF.Functions.DateDiff*.
        //
        // Reason:
        // - DateDiff* methods are supported ONLY by SQL Server provider.
        // - InMemory and SQLite providers do NOT support DateDiff translation, 
        //   causing EF to switch to client-evaluation → InvalidOperationException.
        // - Subtracting DateTime values ((FinishedAt ?? now) - CreatedAt) is fully 
        //   translatable by EF Core for ALL providers:
        //      • SQL Server → translated to DATEDIFF_BIG(...)
        //      • SQLite → translated to julianday(...) math
        //      • InMemory → evaluated safely in-memory without exceptions
        //
        // This makes the query portable, testable, and fully server-evaluated whenever possible.
        var longestTask = await tasks.Query()
            .Where(t => t.PerformerId == userId)
            .OrderByDescending(t =>
                // EF Core 10 can translate DateTime subtraction into provider-specific SQL/time math.
                (t.FinishedAt ?? now) - t.CreatedAt
            )
            .Select(t => new TaskDto(
                t.Id,
                t.Name,
                t.Description,
                t.State == TaskState.ToDo ? "To Do" :
                t.State == TaskState.InProgress ? "In Progress" :
                t.State == TaskState.Done ? "Done" : "Canceled",
                t.CreatedAt,
                t.FinishedAt
            ))
            .FirstOrDefaultAsync();


        return new UserInfoDto(
            userDto,
            lastProject,
            lastProjectTasksCount,
            notFinishedOrCanceledTasksCount,
            longestTask
        );
    }
}
