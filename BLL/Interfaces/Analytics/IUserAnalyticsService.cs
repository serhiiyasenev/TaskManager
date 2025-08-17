using BLL.Models.Users;

namespace BLL.Interfaces.Analytics
{
    public interface IUserAnalyticsService
    {
        Task<List<UserWithTasksDto>> GetSortedUsersWithSortedTasksAsync();
        Task<UserInfoDto> GetUserInfoAsync(int userId);
    }
}
