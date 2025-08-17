using BLL.Models;
using BLL.Models.Projects;

namespace BLL.Interfaces.Analytics
{
    public interface IProjectAnalyticsService
    {
        Task<List<(int Id, string Name, int teamSizeCurrent)>> GetProjectsByTeamSizeAsync(int teamSize);
        Task<List<ProjectInfoDto>> GetProjectsInfoAsync();
        Task<PagedList<FullProjectDto>> GetSortedFilteredPageOfProjectsAsync(PageModel page, FilterModel filter, SortingModel sorting);
    }
}
