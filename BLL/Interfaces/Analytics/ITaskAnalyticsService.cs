using BLL.Models.Tasks;

namespace BLL.Interfaces.Analytics
{
    public interface ITaskAnalyticsService
    {
        Task<Dictionary<string, int>> GetTasksCountInProjectsByUserIdAsync(int userId);
        Task<List<TaskDto>> GetCapitalTasksByUserIdAsync(int userId);
    }
}
