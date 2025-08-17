using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Models.Teams;
using DAL.Entities;
using DAL.Repositories.Interfaces;
using Task = System.Threading.Tasks.Task;

namespace BLL.Services;

public class TeamsService(IRepository<Team> teams, IUnitOfWork uow) : ITeamsService
{
    public async Task<List<Team>> GetTeamsAsync() => await teams.ListAsync();

    public async Task<Team> GetTeamByIdAsync(int id) => await teams.GetByIdAsync(id) ?? throw new NotFoundException(nameof(Team), id);

    public async Task<Team> AddTeamAsync(CreateTeamDto teamDto)
    {
        var team = new Team
        {
            Id = 0,
            Name = teamDto.Name,
            CreatedAt = DateTime.UtcNow
        };
        await teams.AddAsync(team);
        await uow.SaveChangesAsync();
        return team;
    }

    public async Task<Team> UpdateTeamByIdAsync(int id, UpdateTeamDto team)
    {
        var entity = await teams.GetByIdAsync(id) ?? throw new NotFoundException(nameof(Team), id);
        entity.Name = team.Name;
        teams.Update(entity);
        await uow.SaveChangesAsync();
        return entity;
    }

    public async Task DeleteTeamByIdAsync(int id)
    {
        var entity = await teams.GetByIdAsync(id) ?? throw new NotFoundException(nameof(Team), id);
        try
        {
            teams.Remove(entity);
            await uow.SaveChangesAsync();
        }
        catch
        {
            throw new CanNotDeleteException(nameof(Team), id);
        }
    }
}