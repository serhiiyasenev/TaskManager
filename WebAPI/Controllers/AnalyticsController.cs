using BLL.Interfaces;
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
    public class AnalyticsController(IDataProcessingService dataProcessingService) : ControllerBase
    {
        [HttpGet("GetCapitalTasksByUserId/{userId}")]
        [ProducesResponseType(typeof(IEnumerable<TaskDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<TaskDto>>> GetCapitalTasksByUserIdAsync(int userId)
        {
            return Ok(await dataProcessingService.GetCapitalTasksByUserIdAsync(userId));
        }

        [HttpGet("GetProjectsByTeamSize/{teamSize}")]
        [ProducesResponseType(typeof(IEnumerable<ProjectInfo>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<ProjectInfo>>> GetProjectsByTeamSizeAsync(int teamSize)
        {
            var projects = await dataProcessingService.GetProjectsByTeamSizeAsync(teamSize);
            var projectInfoList = projects.Select(p => new ProjectInfo { Id = p.Id, Name = p.Name, TeamSize = p.teamSizeCurrent }).ToList();
            return Ok(projectInfoList);
        }

        [HttpGet("GetProjectsInfo")]
        [ProducesResponseType(typeof(IEnumerable<ProjectInfoDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<ProjectInfoDto>>> GetProjectsInfoAsync()
        {
            return Ok(await dataProcessingService.GetProjectsInfoAsync());
        }

        [HttpGet("GetSortedFilteredPageOfProjects")]
        [ProducesResponseType(typeof(PagedList<FullProjectDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<PagedList<FullProjectDto>>> GetSortedFilteredPageOfProjectsAsync([FromQuery] PageModel? pageModel, [FromQuery] FilterModel? filterModel, [FromQuery] SortingModel sortingModel)
        {
            if (!(pageModel?.PageNumber > 0 && pageModel.PageSize > 0))
            {
                pageModel = null;
            }

            if (!(filterModel?.Name!= null || filterModel?.Description != null || filterModel?.AuthorFirstName != null || filterModel?.AuthorLastName != null || filterModel?.TeamName != null))
            {
                filterModel = null;
            }

            return Ok(await dataProcessingService.GetSortedFilteredPageOfProjectsAsync(pageModel, filterModel, sortingModel));
        }

        [HttpGet("GetSortedTeamByMembersWithYear/{year}")]
        [ProducesResponseType(typeof(IEnumerable<TeamWithMembersDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<TeamWithMembersDto>>> GetSortedTeamByMembersWithYearAsync([FromRoute] int year)
        {
            return Ok(await dataProcessingService.GetSortedTeamByMembersWithYearAsync(year));
        }

        [HttpGet("GetSortedUsersWithSortedTasks")]
        [ProducesResponseType(typeof(IEnumerable<UserWithTasksDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<UserWithTasksDto>>> GetSortedUsersWithSortedTasksAsync()
        {
            return Ok(await dataProcessingService.GetSortedUsersWithSortedTasksAsync());
        }

        [HttpGet("GetTasksCountInProjectsByUserId/{userId}")]
        [ProducesResponseType(typeof(Dictionary<string, int>), StatusCodes.Status200OK)]
        public async Task<ActionResult<Dictionary<string, int>>> GetTasksCountInProjectsByUserIdAsync([FromRoute] int userId)
        {
            return Ok(await dataProcessingService.GetTasksCountInProjectsByUserIdAsync(userId));
        }

        [HttpGet("GetUserInfo/{userId}")]
        [ProducesResponseType(typeof(Dictionary<string, int>), StatusCodes.Status200OK)]
        public async Task<ActionResult<UserInfoDto>> GetUserInfoAsync([FromRoute] int userId)
        {
            return Ok(await dataProcessingService.GetUserInfoAsync(userId));
        }
    }
}