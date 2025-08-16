using BLL.Models;
using BLL.Models.Projects;
using BLL.Models.Tasks;
using BLL.Models.Teams;
using BLL.Models.Users;

namespace BLL.Interfaces;

public interface IDataProcessingService
{
    Task<Dictionary<string, int>> GetTasksCountInProjectsByUserIdAsync(int userId);
    Task<List<TaskDto>> GetCapitalTasksByUserIdAsync(int userId);
    Task<List<(int Id, string Name, int teamSizeCurrent)>> GetProjectsByTeamSizeAsync(int teamSize);
    Task<List<TeamWithMembersDto>> GetSortedTeamByMembersWithYearAsync(int year);
    Task<List<UserWithTasksDto>> GetSortedUsersWithSortedTasksAsync();
    Task<UserInfoDto> GetUserInfoAsync(int userId);
    Task<List<ProjectInfoDto>> GetProjectsInfoAsync();
    Task<PagedList<FullProjectDto>> GetSortedFilteredPageOfProjectsAsync(PageModel pageModel, FilterModel filterModel, SortingModel sortingModel);
}