using AutoMapper;
using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Teams;
using DAL.Entities;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public class TeamsService(
    IRepository<Team> teams,
    IUnitOfWork uow,
    IMapper mapper,
    ILogger<TeamsService> logger) : ITeamsService
{
    public async Task<Result<List<TeamDetailDto>>> GetTeamsAsync(CancellationToken ct = default)
    {
        try
        {
            var teamList = await teams.Query()
                .Include(t => t.Users)
                .AsNoTracking()
                .ToListAsync(ct);
            
            var dtos = mapper.Map<List<TeamDetailDto>>(teamList);
            logger.LogInformation("Retrieved {Count} teams", dtos.Count);
            return Result<List<TeamDetailDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving teams");
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<TeamDetailDto>> GetTeamByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var team = await teams.Query()
                .Include(t => t.Users)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id, ct);
            
            if (team == null)
            {
                logger.LogWarning("Team with ID {TeamId} was not found", id);
                return Error.NotFound("Team", id);
            }

            var dto = mapper.Map<TeamDetailDto>(team);
            logger.LogInformation("Retrieved team {TeamId}", id);
            return Result<TeamDetailDto>.Success(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving team {TeamId}", id);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<Team>> AddTeamAsync(CreateTeamDto teamDto, CancellationToken ct = default)
    {
        try
        {
            var team = new Team
            {
                Id = 0,
                Name = teamDto.Name,
                CreatedAt = DateTime.UtcNow
            };
            await teams.AddAsync(team, ct);
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Created team {TeamName} with ID {TeamId}", team.Name, team.Id);
            return Result<Team>.Success(team);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating team {TeamName}", teamDto.Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<Team>> UpdateTeamByIdAsync(int id, UpdateTeamDto teamDto, CancellationToken ct = default)
    {
        try
        {
            var entity = await teams.GetByIdAsync(id, ct);
            if (entity == null)
            {
                logger.LogWarning("Team with ID {TeamId} was not found for update", id);
                return Error.NotFound("Team", id);
            }

            entity.Name = teamDto.Name;
            teams.Update(entity);
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Updated team {TeamId}", id);
            return Result<Team>.Success(entity);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating team {TeamId}", id);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result> DeleteTeamByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var entity = await teams.GetByIdAsync(id, ct);
            if (entity == null)
            {
                logger.LogWarning("Team with ID {TeamId} was not found for deletion", id);
                return Error.NotFound("Team", id);
            }

            teams.Remove(entity);
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Deleted team {TeamId}", id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting team {TeamId}", id);
            return Error.BusinessRule("Cannot delete team due to existing dependencies");
        }
    }
}