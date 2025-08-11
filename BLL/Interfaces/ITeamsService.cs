using DAL.Entities;
using Task = System.Threading.Tasks.Task;

namespace BLL.Interfaces;

public interface ITeamsService
{
    Task<List<Team>> GetTeamsAsync();
    Task<Team> GetTeamByIdAsync(int id);
    Task<Team> AddTeamAsync(Team team);
    Task<Team> UpdateTeamByIdAsync(int id, Team team);
    Task DeleteTeamByIdAsync(int id);
}
