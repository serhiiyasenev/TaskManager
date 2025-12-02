using BLL.Common;
using BLL.Models.Teams;
using DAL.Entities;

namespace BLL.Interfaces;

public interface ITeamsService
{
    Task<Result<List<Team>>> GetTeamsAsync(CancellationToken ct = default);
    Task<Result<Team>> GetTeamByIdAsync(int id, CancellationToken ct = default);
    Task<Result<Team>> AddTeamAsync(CreateTeamDto team, CancellationToken ct = default);
    Task<Result<Team>> UpdateTeamByIdAsync(int id, UpdateTeamDto team, CancellationToken ct = default);
    Task<Result> DeleteTeamByIdAsync(int id, CancellationToken ct = default);
}
