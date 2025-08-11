using BLL.Exceptions;
using BLL.Interfaces;
using DAL.Entities;
using Task = System.Threading.Tasks.Task;

namespace BLL.Services;

public class TeamsService(IDataProvider dataProvider) : ITeamsService
{
    public async Task<List<Team>> GetTeamsAsync()
    {
        return await dataProvider.GetTeamsAsync();
    }

    public async Task<Team> GetTeamByIdAsync(int id)
    {
        return await dataProvider.GetTeamByIdAsync(id) ?? throw new NotFoundException(nameof(Team), id);
    }

    public async Task<Team> AddTeamAsync(Team team)
    {
        return await dataProvider.AddTeamAsync(team);
    }

    public async Task<Team> UpdateTeamByIdAsync(int id, Team team)
    {
        return await dataProvider.UpdateTeamByIdAsync(id, team) ?? throw new NotFoundException(nameof(Team), id);
    }

    public async Task DeleteTeamByIdAsync(int id)
    {
        var isDeleted = await dataProvider.DeleteTeamByIdAsync(id) ?? throw new NotFoundException(nameof(Team), id);
        if (isDeleted == false) throw new CanNotDeleteException(nameof(Team), id);
    }
}