using BLL.Models.Teams;

namespace BLL.Interfaces.Analytics
{
    public interface ITeamAnalyticsService
    {
        Task<List<TeamWithMembersDto>> GetSortedTeamByMembersWithYearAsync(int year);
    }
}
