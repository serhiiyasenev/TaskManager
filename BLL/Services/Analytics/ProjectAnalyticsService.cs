using BLL.Interfaces.Analytics;
using BLL.Models;
using BLL.Models.Projects;
using BLL.Models.Tasks;
using BLL.Models.Teams;
using BLL.Models.Users;
using DAL.Entities;
using DAL.Enum;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services.Queries;

public class ProjectAnalyticsService(
    IReadRepository<Project> projects,
    IReadRepository<User> users,
    IReadRepository<Team> teams,
    IReadRepository<DAL.Entities.Task> tasks) : IProjectAnalyticsService
{
    public async Task<List<(int Id, string Name, int teamSizeCurrent)>> GetProjectsByTeamSizeAsync(int teamSize)
    {
        var data = await (from p in projects.Query()
                          join t in teams.Query() on p.TeamId equals t.Id
                          join u in users.Query() on t.Id equals u.TeamId into ug
                          select new { p.Id, p.Name, TeamSize = ug.Count() })
                         .Where(x => x.TeamSize >= teamSize)
                         .ToListAsync();

        return data.Select(x => (x.Id, x.Name, x.TeamSize)).ToList();
    }

    public async Task<List<ProjectInfoDto>> GetProjectsInfoAsync()
    {
        return await (from p in projects.Query()
                      select new ProjectInfoDto(
                          new ProjectDto(p.Id, p.Name, p.Description, p.CreatedAt, p.Deadline),

                          (from t in tasks.Query()
                           where t.ProjectId == p.Id
                           orderby t.Description.Length descending
                           select new TaskDto(
                               t.Id, t.Name, t.Description,
                               t.State == TaskState.ToDo ? "To Do" :
                               t.State == TaskState.InProgress ? "In Progress" :
                               t.State == TaskState.Done ? "Done" : "Canceled",
                               t.CreatedAt, t.FinishedAt)).FirstOrDefault(),

                          (from t in tasks.Query()
                           where t.ProjectId == p.Id
                           orderby t.Name.Length
                           select new TaskDto(
                               t.Id, t.Name, t.Description,
                               t.State == TaskState.ToDo ? "To Do" :
                               t.State == TaskState.InProgress ? "In Progress" :
                               t.State == TaskState.Done ? "Done" : "Canceled",
                               t.CreatedAt, t.FinishedAt)).FirstOrDefault(),

                          (p.Description.Length > 20
                              || tasks.Query().Count(t => t.ProjectId == p.Id) < 3)
                              ? (int?)users.Query().Count(u => u.TeamId == p.TeamId)
                              : null))
                     .ToListAsync();
    }

    public async Task<PagedList<FullProjectDto>> GetSortedFilteredPageOfProjectsAsync(
        PageModel page, FilterModel filter, SortingModel sorting)
    {
        var queryable = from p in projects.Query()
                select new
                {
                    p.Id,
                    p.Name,
                    p.Description,
                    p.CreatedAt,
                    p.Deadline,
                    p.AuthorId,
                    p.TeamId,
                    AuthorFirstName = users.Query().Where(u => u.Id == p.AuthorId).Select(u => u.FirstName).FirstOrDefault(),
                    AuthorLastName = users.Query().Where(u => u.Id == p.AuthorId).Select(u => u.LastName).FirstOrDefault(),
                    TeamName = teams.Query().Where(t => t.Id == p.TeamId).Select(t => t.Name).FirstOrDefault(),
                    TasksCount = tasks.Query().Count(t => t.ProjectId == p.Id)
                };

        if (filter is not null)
        {
            if (!string.IsNullOrWhiteSpace(filter.Name))
                queryable = queryable.Where(x => x.Name.Contains(filter.Name));
            if (!string.IsNullOrWhiteSpace(filter.Description))
                queryable = queryable.Where(x => x.Description.Contains(filter.Description));
            if (!string.IsNullOrWhiteSpace(filter.AuthorFirstName))
                queryable = queryable.Where(x => x.AuthorFirstName!.Contains(filter.AuthorFirstName));
            if (!string.IsNullOrWhiteSpace(filter.AuthorLastName))
                queryable = queryable.Where(x => x.AuthorLastName!.Contains(filter.AuthorLastName));
            if (!string.IsNullOrWhiteSpace(filter.TeamName))
                queryable = queryable.Where(x => x.TeamName!.Contains(filter.TeamName));
        }

        if (sorting is not null)
        {
            queryable = sorting.Property switch
            {
                SortingProperty.TasksCount => (sorting.Order == SortingOrder.Ascending) ? queryable.OrderBy(x => x.TasksCount) : queryable.OrderByDescending(x => x.TasksCount),
                SortingProperty.AuthorFirstName => (sorting.Order == SortingOrder.Ascending) ? queryable.OrderBy(x => x.AuthorFirstName) : queryable.OrderByDescending(x => x.AuthorFirstName),
                SortingProperty.AuthorLastName => (sorting.Order == SortingOrder.Ascending) ? queryable.OrderBy(x => x.AuthorLastName) : queryable.OrderByDescending(x => x.AuthorLastName),
                SortingProperty.Description => (sorting.Order == SortingOrder.Ascending) ? queryable.OrderBy(x => x.Description) : queryable.OrderByDescending(x => x.Description),
                SortingProperty.Deadline => (sorting.Order == SortingOrder.Ascending) ? queryable.OrderBy(x => x.Deadline) : queryable.OrderByDescending(x => x.Deadline),
                SortingProperty.CreatedAt => (sorting.Order == SortingOrder.Ascending) ? queryable.OrderBy(x => x.CreatedAt) : queryable.OrderByDescending(x => x.CreatedAt),
                SortingProperty.TeamName => (sorting.Order == SortingOrder.Ascending) ? queryable.OrderBy(x => x.TeamName) : queryable.OrderByDescending(x => x.TeamName),
                _ => (sorting.Order == SortingOrder.Ascending) ? queryable.OrderBy(x => x.Name) : queryable.OrderByDescending(x => x.Name)
            };
        }

        var total = await queryable.CountAsync();

        var pageRows = await queryable
            .Skip((page.PageNumber - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync();

        var projectIds = pageRows.Select(x => x.Id).ToList();

        var taskRows = await (from t in tasks.Query()
                              where projectIds.Contains(t.ProjectId)
                              join u in users.Query() on t.PerformerId equals u.Id into ju
                              from u in ju.DefaultIfEmpty()
                              select new
                              {
                                  t.Id,
                                  t.ProjectId,
                                  t.Name,
                                  t.Description,
                                  t.State,
                                  t.CreatedAt,
                                  t.FinishedAt,
                                  Performer = u == null ? null :
                                      new UserDto(u.Id, u.FirstName, u.LastName, u.Email, u.RegisteredAt, u.BirthDay)
                              }).ToListAsync();

        var tasksByProject = taskRows
            .GroupBy(r => r.ProjectId)
            .ToDictionary(g => g.Key, g => g.Select(r =>
                new TaskWithPerformerDto(
                    r.Id, r.Name, r.Description,
                    r.State == TaskState.ToDo ? "To Do" :
                    r.State == TaskState.InProgress ? "In Progress" :
                    r.State == TaskState.Done ? "Done" : "Canceled",
                    r.CreatedAt, r.FinishedAt, r.Performer)).ToList());

        var items = pageRows.Select(x =>
            new FullProjectDto(
                x.Id, x.Name, x.Description, x.CreatedAt, x.Deadline,
                tasksByProject.GetValueOrDefault(x.Id, []),
                new UserDto(
                    users.Query().Where(u => u.Id == x.AuthorId)
                        .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.RegisteredAt, u.BirthDay })
                        .FirstOrDefault()!.Id,
                    x.AuthorFirstName!, x.AuthorLastName!,
                    users.Query().Where(u => u.Id == x.AuthorId).Select(u => u.Email).FirstOrDefault()!,
                    users.Query().Where(u => u.Id == x.AuthorId).Select(u => u.RegisteredAt).FirstOrDefault(),
                    users.Query().Where(u => u.Id == x.AuthorId).Select(u => u.BirthDay).FirstOrDefault()
                ),
                new TeamDto(
                    x.TeamId,
                    x.TeamName!,
                    teams.Query().Where(t => t.Id == x.TeamId).Select(t => t.CreatedAt).FirstOrDefault())))
            .ToList();

        return new PagedList<FullProjectDto>(items, total);
    }
}
