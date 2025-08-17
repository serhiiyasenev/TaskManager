using BLL.Models.Teams;
using DAL.Entities;
using Task = System.Threading.Tasks.Task;

namespace BLL.Interfaces;

public interface ITeamsService
{
    Task<List<Team>> GetTeamsAsync();
    Task<Team> GetTeamByIdAsync(int id);
    Task<Team> AddTeamAsync(CreateTeamDto team);
    Task<Team> UpdateTeamByIdAsync(int id, UpdateTeamDto team);
    Task DeleteTeamByIdAsync(int id);
}
