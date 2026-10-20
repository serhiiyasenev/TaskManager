#nullable enable annotations
using AutoMapper;
using BLL.Authorization;
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
    ILogger<TeamsService> logger,
    IUserAccess? access = null,
    ITeamManagementService? management = null) : ITeamsService
{
    public async Task<Result<List<TeamDetailDto>>> GetTeamsAsync(CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            var teamList = await teams.Query().VisibleTeams(scope)
                .Include(t => t.Users)
                .AsNoTracking()
                .ToListAsync(ct);
            
            var dtos = mapper.Map<List<TeamDetailDto>>(teamList);
            logger.LogInformation("Retrieved {Count} teams", dtos.Count);
            return Result<List<TeamDetailDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError("Error retrieving teams ({FailureType}).", ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<TeamDetailDto>> GetTeamByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            var team = await teams.Query().VisibleTeams(scope)
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
            logger.LogError("Error retrieving team {TeamId} ({FailureType}).", id, ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<Team>> AddTeamAsync(CreateTeamDto teamDto, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            if (access is not null)
                return management is null ? Error.UnexpectedError : await management.CreateTeamAsync(teamDto.Name, ct);
            var team = new Team
            {
                Id = 0,
                Name = teamDto.Name,
                CreatedAt = DateTime.UtcNow
            };
            await teams.AddAsync(team, ct);
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Created team {TeamId}", team.Id);
            return Result<Team>.Success(team);
        }
        catch (Exception ex)
        {
            logger.LogError("Error creating team ({FailureType}).", ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<Team>> UpdateTeamByIdAsync(int id, UpdateTeamDto teamDto, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            if (access is not null)
                return management is null ? Error.UnexpectedError : await management.UpdateTeamAsync(id, teamDto.Name, ct);
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
            logger.LogError("Error updating team {TeamId} ({FailureType}).", id, ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result> DeleteTeamByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            if (access is not null)
                return management is null ? Error.UnexpectedError : await management.DeleteTeamAsync(id, ct);
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
            logger.LogError("Error deleting team {TeamId} ({FailureType}).", id, ex.GetType().Name);
            return Error.BusinessRule("Cannot delete team due to existing dependencies");
        }
    }
}
