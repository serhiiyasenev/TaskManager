using BLL.Interfaces.Analytics;
using BLL.Models.Teams;
using BLL.Models.Users;
using DAL.Entities;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services.Analytics;

public class TeamAnalyticsService(
    IReadRepository<Team> teams,
    IReadRepository<User> users) : ITeamAnalyticsService
{
    public async Task<List<TeamWithMembersDto>> GetSortedTeamByMembersWithYearAsync(int year)
    {
        var rows = await (from t in teams.Query()
            join u in users.Query() on t.Id equals u.TeamId
            where u.BirthDay.Year < year
            orderby t.Name, u.RegisteredAt descending
            select new
            {
                t.Id,
                t.Name,
                Member = new UserDto(u.Id, u.TeamId, u.FirstName, u.LastName, u.Email, u.RegisteredAt, u.BirthDay)
            }).ToListAsync();

        return rows.GroupBy(r => new { r.Id, r.Name })
            .Select(g => new TeamWithMembersDto(g.Key.Id, g.Key.Name, g.Select(x => x.Member).ToList()))
            .OrderBy(x => x.Name)
            .ToList();
    }
}