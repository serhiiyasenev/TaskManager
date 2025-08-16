using BLL.Interfaces;
using BLL.Models;
using BLL.Models.Projects;
using BLL.Models.Tasks;
using BLL.Models.Teams;
using BLL.Models.Users;
using DAL.Enum;

namespace BLL.Services;

public class DataProcessingService(IDataProvider dataProvider) : IDataProcessingService
{
    public async Task<Dictionary<string, int>> GetTasksCountInProjectsByUserIdAsync(int userId)
    {
        var projects = await dataProvider.GetProjectsAsync();
        var tasks = await dataProvider.GetTasksAsync();

        var result = tasks.Where(t => t.PerformerId == userId)
            .Join(projects,
                t => t.ProjectId,
                p => p.Id,
                (t, p) => new { Task = t, Project = p })
            .GroupBy(tp => new { tp.Project.Id, tp.Project.Name })
            .ToDictionary(g => $"{g.Key.Id}: {g.Key.Name}", g => g.Count());

        return result;
    }

    public async Task<List<TaskDto>> GetCapitalTasksByUserIdAsync(int userId)
    {
        var tasks = await dataProvider.GetTasksAsync();

        var result = tasks
            .Where(t => t.PerformerId == userId && char.IsUpper(t.Name[0]))
            .Select(entity => new TaskDto(
                entity.Id,
                entity.Name,
                entity.Description,
                MapTaskStateToString(entity.State),
                entity.CreatedAt,
                entity.FinishedAt))
            .ToList();

        return result;
    }

    public async Task<List<(int Id, string Name, int teamSizeCurrent)>> GetProjectsByTeamSizeAsync(int teamSize)
    {
        var users = await dataProvider.GetUsersAsync();
        var projects = await dataProvider.GetProjectsAsync();
        var teams = await dataProvider.GetTeamsAsync();

        var result = (from project in projects
                      join team in teams on project.TeamId equals team.Id
                      let teamSizeCurrent = users.Count(u => u.TeamId == team.Id)
                      where teamSizeCurrent >= teamSize
                      select (project.Id, project.Name, teamSizeCurrent)).ToList();

        return result;
    }

    public async Task<List<TeamWithMembersDto>> GetSortedTeamByMembersWithYearAsync(int year)
    {
        var teams = await dataProvider.GetTeamsAsync();
        var users = await dataProvider.GetUsersAsync();

        var result = teams
            .Select(team => new
            {
                Team = team,
                Members = users
                    .Where(user => user.TeamId == team.Id && user.BirthDay.Year < year)
                    .OrderByDescending(user => user.RegisteredAt)
                    .Select(user => new UserDto(user.Id, user.FirstName, user.LastName, user.Email, user.RegisteredAt, user.BirthDay))
                    .ToList()
            })
            .Where(teamWithMembers => teamWithMembers.Members.Any())
            .Select(teamWithMembers => new TeamWithMembersDto(teamWithMembers.Team.Id, teamWithMembers.Team.Name, teamWithMembers.Members))
            .OrderBy(teamWithMembers => teamWithMembers.Name)
            .ToList();

        return result;
    }

    public async Task<List<UserWithTasksDto>> GetSortedUsersWithSortedTasksAsync()
    {
        var users = await dataProvider.GetUsersAsync();
        var tasks = await dataProvider.GetTasksAsync();

        var result = users
            .Select(user => new
            {
                User = user,
                Tasks = tasks
                    .Where(task => task.PerformerId == user.Id)
                    .OrderByDescending(task => task.Name.Length)
                    .Select(task => new TaskDto(task.Id, task.Name, task.Description, MapTaskStateToString(task.State), task.CreatedAt, task.FinishedAt))
                    .ToList()
            })
            .OrderBy(userWithTasks => userWithTasks.User.FirstName)
            .Select(userWithTasks => new UserWithTasksDto(userWithTasks.User.Id, userWithTasks.User.FirstName, userWithTasks.User.LastName, userWithTasks.User.Email, userWithTasks.User.RegisteredAt, userWithTasks.User.BirthDay, userWithTasks.Tasks))
            .ToList();

        return result;
    }

    public async Task<UserInfoDto> GetUserInfoAsync(int userId)
    {
        var users = await dataProvider.GetUsersAsync();
        var user = users.FirstOrDefault(u => u.Id == userId);
        if (user == null) return null;

        var projects = await dataProvider.GetProjectsAsync();
        var tasks = await dataProvider.GetTasksAsync();

        var lastProject = projects.OrderByDescending(p => p.CreatedAt).FirstOrDefault(p => p.AuthorId == userId);
        var lastProjectDto = lastProject != null ? new ProjectDto(lastProject.Id, lastProject.Name, lastProject.Description, lastProject.CreatedAt, lastProject.Deadline) : null;
        var lastProjectTasksCount = lastProject != null ? tasks.Count(t => t.ProjectId == lastProject.Id) : 0;
        var notFinishedOrCanceledTasksCount = tasks.Count(t => t.PerformerId == userId && (t.State == TaskState.ToDo || t.State == TaskState.InProgress || t.State == TaskState.Canceled));
        var longestTask = tasks.Where(t => t.PerformerId == userId).OrderByDescending(t => (t.FinishedAt ?? DateTime.UtcNow) - t.CreatedAt).FirstOrDefault();
        var longestTaskDto = longestTask != null ? new TaskDto(longestTask.Id, longestTask.Name, longestTask.Description, MapTaskStateToString(longestTask.State), longestTask.CreatedAt, longestTask.FinishedAt) : null;

        var userInfo = new UserInfoDto(
            new UserDto(user.Id, user.FirstName, user.LastName, user.Email, user.RegisteredAt, user.BirthDay),
            lastProjectDto,
            lastProjectTasksCount,
            notFinishedOrCanceledTasksCount,
            longestTaskDto);

        return userInfo;
    }

    public async Task<List<ProjectInfoDto>> GetProjectsInfoAsync()
    {
        var projects = await dataProvider.GetProjectsAsync();
        var tasks = await dataProvider.GetTasksAsync();
        var teams = await dataProvider.GetTeamsAsync();
        var users = await dataProvider.GetUsersAsync();

        var result = projects.Select(project =>
        {
            var projectDto = new ProjectDto(project.Id, project.Name, project.Description, project.CreatedAt, project.Deadline);

            var projectTasks = tasks.Where(t => t.ProjectId == project.Id).ToList();
            var longestTaskByDescription = projectTasks.OrderByDescending(t => t.Description.Length).FirstOrDefault();
            var shortestTaskByName = projectTasks.OrderBy(t => t.Name.Length).FirstOrDefault();

            var longestTaskByDescriptionDto = longestTaskByDescription != null ? new TaskDto(longestTaskByDescription.Id, longestTaskByDescription.Name, longestTaskByDescription.Description, MapTaskStateToString(longestTaskByDescription.State), longestTaskByDescription.CreatedAt, longestTaskByDescription.FinishedAt) : null;
            var shortestTaskByNameDto = shortestTaskByName != null ? new TaskDto(shortestTaskByName.Id, shortestTaskByName.Name, shortestTaskByName.Description, MapTaskStateToString(shortestTaskByName.State), shortestTaskByName.CreatedAt, shortestTaskByName.FinishedAt) : null;

            int? teamMembersCount = null;
            if (project.Description.Length > 20 || projectTasks.Count < 3)
            {
                var team = teams.FirstOrDefault(t => t.Id == project.TeamId);
                if (team != null)
                    teamMembersCount = users.Count(u => u.TeamId == team.Id);
            }

            return new ProjectInfoDto(projectDto, longestTaskByDescriptionDto, shortestTaskByNameDto, teamMembersCount);

        }).ToList();

        return result;
    }

    public async Task<PagedList<FullProjectDto>> GetSortedFilteredPageOfProjectsAsync(PageModel pageModel, FilterModel filterModel, SortingModel sortingModel)
    {
        var projects = await dataProvider.GetProjectsAsync();
        var tasks = await dataProvider.GetTasksAsync();
        var users = await dataProvider.GetUsersAsync();
        var teams = await dataProvider.GetTeamsAsync();

        // Filtering
        if (filterModel != null)
        {
            projects = projects.Where(p =>
                (string.IsNullOrEmpty(filterModel.Name) || p.Name.Contains(filterModel.Name, StringComparison.InvariantCultureIgnoreCase)) &&
                (string.IsNullOrEmpty(filterModel.Description) || p.Description.Contains(filterModel.Description, StringComparison.InvariantCultureIgnoreCase)) &&
                (string.IsNullOrEmpty(filterModel.AuthorFirstName) || users.Any(u => u.Id == p.AuthorId && u.FirstName.Contains(filterModel.AuthorFirstName, StringComparison.InvariantCultureIgnoreCase))) &&
                (string.IsNullOrEmpty(filterModel.AuthorLastName) || users.Any(u => u.Id == p.AuthorId && u.LastName.Contains(filterModel.AuthorLastName, StringComparison.InvariantCultureIgnoreCase))) &&
                (string.IsNullOrEmpty(filterModel.TeamName) || teams.Any(t => t.Id == p.TeamId && t.Name.Contains(filterModel.TeamName, StringComparison.InvariantCultureIgnoreCase))))
                .ToList();
        }

        // Sorting
        if (sortingModel != null)
        {
            switch (sortingModel.Property)
            {
                case SortingProperty.TasksCount:
                    projects = sortingModel.Order == SortingOrder.Ascending ?
                        projects.OrderBy(p => tasks.Count(t => t.ProjectId == p.Id)).ToList() :
                        projects.OrderByDescending(p => tasks.Count(t => t.ProjectId == p.Id)).ToList();
                    break;
                case SortingProperty.AuthorFirstName:
                    projects = sortingModel.Order == SortingOrder.Ascending ?
                        projects.OrderBy(p => users.FirstOrDefault(u => u.Id == p.AuthorId)?.FirstName).ToList() :
                        projects.OrderByDescending(p => users.FirstOrDefault(u => u.Id == p.AuthorId)?.FirstName).ToList();
                    break;
                case SortingProperty.AuthorLastName:
                    projects = sortingModel.Order == SortingOrder.Ascending ?
                        projects.OrderBy(p => users.FirstOrDefault(u => u.Id == p.AuthorId)?.LastName).ToList() :
                        projects.OrderByDescending(p => users.FirstOrDefault(u => u.Id == p.AuthorId)?.LastName).ToList();
                    break;
                case SortingProperty.Name:
                    projects = sortingModel.Order == SortingOrder.Ascending ?
                        projects.OrderBy(p => projects.FirstOrDefault(u => u.Id == p.Id)?.Name).ToList() :
                        projects.OrderByDescending(p => projects.FirstOrDefault(u => u.Id == p.Id)?.Name).ToList();
                    break;
            }
        }

        // Total Count
        var totalCount = projects.Count;

        // Paging
        if (pageModel != null)
        {
            projects = projects
                .Skip((pageModel.PageNumber - 1) * pageModel.PageSize)
                .Take(pageModel.PageSize)
                .ToList();
        }

        // FullProjectDto for each project
        var fullProjects = projects.Select(project =>
        {
            var tasksOfProject = tasks.Where(t => t.ProjectId == project.Id).ToList();

            var tasksWithPerformer = tasksOfProject
                .Select(task =>
                {
                    var performer = users.FirstOrDefault(u => u.Id == task.PerformerId);
                    var performerDto = performer != null ? new UserDto(performer.Id, performer.FirstName, performer.LastName, performer.Email, performer.RegisteredAt, performer.BirthDay) : null;
                    return new TaskWithPerformerDto(
                        task.Id,
                        task.Name,
                        task.Description,
                        MapTaskStateToString(task.State),
                        task.CreatedAt,
                        task.FinishedAt,
                        performerDto);
                })
                .ToList();

            var author = users.FirstOrDefault(u => u.Id == project.AuthorId);
            var team = teams.FirstOrDefault(t => t.Id == project.TeamId);

            return new FullProjectDto(
                project.Id,
                project.Name,
                project.Description,
                project.CreatedAt,
                project.Deadline,
                tasksWithPerformer,
                author != null ? new UserDto(author.Id, author.FirstName, author.LastName, author.Email, author.RegisteredAt, author.BirthDay) : null,
                team != null ? new TeamDto(team.Id, team.Name, team.CreatedAt) : null);

        }).ToList();

        return new PagedList<FullProjectDto>(fullProjects, totalCount);
    }

    private string MapTaskStateToString(TaskState state)
    {
        return state switch
        {
            TaskState.ToDo => "To Do",
            TaskState.InProgress => "In Progress",
            TaskState.Done => "Done",
            TaskState.Canceled => "Canceled",
            _ => throw new InvalidOperationException("Unknown task state")
        };
    }
}
