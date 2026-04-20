using System.Diagnostics.CodeAnalysis;
using BLL.Interfaces.Analytics;
using BLL.Models.Projects;
using BLL.Models.Tasks;
using DAL.Entities;
using DAL.Enum;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services.Analytics;

[ExcludeFromCodeCoverage]
public class TaskAnalyticsService(
    IReadRepository<DAL.Entities.Task> tasks,
    IReadRepository<Project> projects) : ITaskAnalyticsService
{
    public async Task<Dictionary<string, int>> GetTasksCountInProjectsByUserIdAsync(int userId)
    {
        var rows = await (
                from p in projects.Query()
                join t in tasks.Query().Where(t => t.PerformerId == userId)
                    on p.Id equals t.ProjectId into tg
                select new
                {
                    p.Id,
                    p.Name,
                    Count = tg.Count()
                }).ToListAsync();

        return rows.ToDictionary(x => $"{x.Id}: {x.Name}", x => x.Count);
    }

    public async Task<List<TaskDto>> GetCapitalTasksByUserIdAsync(int userId)
    {
        var rows = await tasks.Query()
            .Where(t => t.PerformerId == userId)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Description,
                t.State,
                t.CreatedAt,
                t.FinishedAt,
                t.DueDate,
                t.ReminderEnabled,
                t.ReminderOffsetMinutes,
                t.EscalationEnabled,
                t.EscalationDelayMinutes,
                t.ReminderSentAt,
                t.EscalationSentAt
            })
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
                x.CreatedAt,
                x.FinishedAt,
                x.DueDate,
                x.ReminderEnabled,
                x.ReminderOffsetMinutes,
                x.EscalationEnabled,
                x.EscalationDelayMinutes,
                x.ReminderSentAt,
                x.EscalationSentAt))
            .ToList();
    }

    public async Task<List<ProjectTaskStatusDto>> GetTaskStatusByProjectAsync(int projectId)
    {
        var query =
            from p in projects.Query()
            where p.Id == projectId
            join t in tasks.Query() on p.Id equals t.ProjectId into tg
            orderby p.Name
            select new ProjectTaskStatusDto(
                p.Id,
                p.Name,
                tg.Count(x => x.State == TaskState.ToDo),
                tg.Count(x => x.State == TaskState.InProgress),
                tg.Count(x => x.State == TaskState.Done),
                tg.Count(x => x.State == TaskState.Canceled),
                tg.Count()
            );

        return await query.ToListAsync();
    }
}
