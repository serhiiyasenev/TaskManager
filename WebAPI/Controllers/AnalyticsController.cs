using BLL.Interfaces.Analytics;
using BLL.Models;
using BLL.Models.Projects;
using BLL.Models.Tasks;
using BLL.Models.Teams;
using BLL.Models.Users;
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalyticsController(
        ITaskAnalyticsService taskAnalytics,
        IProjectAnalyticsService projectQueries,
        ITeamAnalyticsService teamQueries,
        IUserAnalyticsService userQueries)
        : ControllerBase
    {
        [HttpGet("GetCapitalTasksByUserId/{userId:int}")]
        [ProducesResponseType(typeof(IEnumerable<TaskDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<TaskDto>>> GetCapitalTasksByUserIdAsync(int userId)
        {
            return Ok(await taskAnalytics.GetCapitalTasksByUserIdAsync(userId));
        }

        [HttpGet("GetProjectsByTeamSize/{teamSize:int}")]
        [ProducesResponseType(typeof(IEnumerable<ProjectInfo>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<ProjectInfo>>> GetProjectsByTeamSizeAsync(int teamSize)
        {
            var projects = await projectQueries.GetProjectsByTeamSizeAsync(teamSize);
            var projectInfoList = projects.Select(p => new ProjectInfo { Id = p.Id, Name = p.Name, TeamSize = p.teamSizeCurrent }).ToList();
            return Ok(projectInfoList);
        }

        [HttpGet("GetProjectsInfo")]
        [ProducesResponseType(typeof(IEnumerable<ProjectInfoDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<ProjectInfoDto>>> GetProjectsInfoAsync()
        {
            return Ok(await projectQueries.GetProjectsInfoAsync());
        }

        [HttpGet("GetSortedFilteredPageOfProjects")]
        [ProducesResponseType(typeof(PagedList<FullProjectDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<PagedList<FullProjectDto>>> GetSortedFilteredPageOfProjectsAsync([FromQuery] PageModel? pageModel, [FromQuery] FilterModel? filterModel, [FromQuery] SortingModel sortingModel)
        {
            if (pageModel is not { PageNumber: > 0, PageSize: > 0 })
                pageModel = new PageModel(20, 1);

            if (filterModel is { Name: null, Description: null, AuthorFirstName: null, AuthorLastName: null, TeamName: null })
                filterModel = null;

            return Ok(await projectQueries.GetSortedFilteredPageOfProjectsAsync(pageModel, filterModel, sortingModel));
        }

        [HttpGet("GetSortedTeamByMembersWithYear/{year:int}")]
        [ProducesResponseType(typeof(IEnumerable<TeamWithMembersDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<TeamWithMembersDto>>> GetSortedTeamByMembersWithYearAsync([FromRoute] int year)
        {
            return Ok(await teamQueries.GetSortedTeamByMembersWithYearAsync(year));
        }

        [HttpGet("GetSortedUsersWithSortedTasks")]
        [ProducesResponseType(typeof(IEnumerable<UserWithTasksDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<UserWithTasksDto>>> GetSortedUsersWithSortedTasksAsync()
        {
            return Ok(await userQueries.GetSortedUsersWithSortedTasksAsync());
        }

        [HttpGet("GetTasksCountInProjectsByUserId/{userId:int}")]
        [ProducesResponseType(typeof(Dictionary<string, int>), StatusCodes.Status200OK)]
        public async Task<ActionResult<Dictionary<string, int>>> GetTasksCountInProjectsByUserIdAsync([FromRoute] int userId)
        {
            return Ok(await taskAnalytics.GetTasksCountInProjectsByUserIdAsync(userId));
        }

        [HttpGet("GetUserInfo/{userId:int}")]
        [ProducesResponseType(typeof(UserInfoDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<UserInfoDto>> GetUserInfoAsync([FromRoute] int userId)
        {
            return Ok(await userQueries.GetUserInfoAsync(userId));
        }

        [HttpGet("GetTaskStatusByProject/{projectId:int}")]
        [ProducesResponseType(typeof(IEnumerable<ProjectTaskStatusDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<ProjectTaskStatusDto>>> GetTaskStatusByProjectAsync([FromRoute] int projectId)
        {
            return Ok(await taskAnalytics.GetTaskStatusByProjectAsync(projectId));
        }
    }
}