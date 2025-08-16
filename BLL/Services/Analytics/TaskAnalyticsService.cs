using BLL.Interfaces.Analytics;
using BLL.Models.Tasks;
using DAL.Entities;
using DAL.Enum;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services.Queries;

public class TaskAnalyticsService(
    IReadRepository<DAL.Entities.Task> tasks,
    IReadRepository<Project> projects) : ITaskAnalyticsService
{
    public async Task<Dictionary<string, int>> GetTasksCountInProjectsByUserIdAsync(int userId)
    {
        return await (from t in tasks.Query()
                where t.PerformerId == userId
                join p in projects.Query() on t.ProjectId equals p.Id
                group t by new { p.Id, p.Name } into g
                select new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => $"{x.Key.Id}: {x.Key.Name}", x => x.Count);
    }

    public async Task<List<TaskDto>> GetCapitalTasksByUserIdAsync(int userId)
    {
        var rows = await tasks.Query()
            .Where(t => t.PerformerId == userId)
            .Select(t => new { t.Id, t.Name, t.Description, t.State, t.CreatedAt, t.FinishedAt })
            .ToListAsync();

        return rows
            .Where(x => !string.IsNullOrEmpty(x.Name) && char.IsUpper(x.Name[0]))
            .Select(x => new TaskDto(
                x.Id, x.Name, x.Description,
                x.State switch
                {
                    TaskState.ToDo => "To Do",
                    TaskState.InProgress => "In Progress",
                    TaskState.Done => "Done",
                    TaskState.Canceled => "Canceled",
                    _ => "Unknown"
                },
                x.CreatedAt, x.FinishedAt))
            .ToList();
    }
}