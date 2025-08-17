using BLL.Models.Projects;
using BLL.Models.Tasks;

namespace BLL.Interfaces.Analytics
{
    public interface ITaskAnalyticsService
    {
        Task<Dictionary<string, int>> GetTasksCountInProjectsByUserIdAsync(int userId);
        Task<List<TaskDto>> GetCapitalTasksByUserIdAsync(int userId);
        Task<List<ProjectTaskStatusDto>> GetTaskStatusByProjectAsync(int projectId);
    }
}
