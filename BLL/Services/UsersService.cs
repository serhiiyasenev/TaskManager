#nullable enable annotations
using AutoMapper;
using BLL.Authorization;
using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Users;
using DAL.Entities;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public class UsersService(
    IRepository<User> users,
    IReadRepository<Team> teams,
    IUnitOfWork uow,
    IMapper mapper,
    ILogger<UsersService> logger,
    IUserAccess? access = null) : IUsersService
{
    public async Task<Result<List<UserDetailDto>>> GetUsersAsync(CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            var entities = await QueryWithDetails(scope).ToListAsync(ct);
            
            var dtos = mapper.Map<List<UserDetailDto>>(entities);
            logger.LogInformation("Retrieved {Count} users", dtos.Count);
            return Result<List<UserDetailDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError("Error retrieving users ({FailureType}).", ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<UserDetailDto>> GetUserByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            var user = await QueryWithDetails(scope).FirstOrDefaultAsync(u => u.Id == id, ct);
            
            if (user == null)
            {
                logger.LogWarning("User with ID {UserId} was not found", id);
                return Error.NotFound("User", id);
            }

            var dto = mapper.Map<UserDetailDto>(user);
            logger.LogInformation("Retrieved user {UserId}", id);
            return Result<UserDetailDto>.Success(dto);
        }
        catch (Exception ex)
        {
            logger.LogError("Error retrieving user {UserId} ({FailureType}).", id, ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<UserDto>> UpdateUserByIdAsync(int id, UpdateUserDto userDto, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            if (scope is { IsAdmin: false } && scope.UserId != id) return Error.Forbidden;
            var entity = await users.GetByIdAsync(id, ct);
            if (entity == null)
            {
                logger.LogWarning("User with ID {UserId} was not found for update", id);
                return Error.NotFound("User", id);
            }

            if (scope is not null)
            {
                if (!scope.IsAdmin && userDto.TeamId != entity.TeamId) return Error.Forbidden;
                if (!string.Equals(userDto.Email, entity.Email, StringComparison.OrdinalIgnoreCase))
                    return Error.Validation("Email", "Use the confirmed email change workflow to change the account email.");
            }

            if (userDto.TeamId.HasValue && !await teams.AnyAsync(t => t.Id == userDto.TeamId.Value, ct))
            {
                logger.LogWarning("Team with ID {TeamId} was not found", userDto.TeamId.Value);
                return Error.NotFound("Team", userDto.TeamId.Value);
            }

            entity.TeamId = userDto.TeamId;
            entity.FirstName = userDto.FirstName;
            entity.LastName = userDto.LastName;
            if (scope is null) entity.Email = userDto.Email;
            entity.BirthDay = userDto.BirthDay;

            users.Update(entity);
            await uow.SaveChangesAsync(ct);

            var dto = new UserDto(entity.Id, entity.TeamId, entity.FirstName, entity.LastName, entity.Email, entity.RegisteredAt, entity.BirthDay);
            logger.LogInformation("Updated user {UserId}", id);
            return Result<UserDto>.Success(dto);
        }
        catch (Exception ex)
        {
            logger.LogError("Error updating user {UserId} ({FailureType}).", id, ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result> DeleteUserByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            if (scope is { IsAdmin: false } && scope.UserId != id) return Error.Forbidden;
            var entity = await users.GetByIdAsync(id, ct);
            if (entity == null)
            {
                logger.LogWarning("User with ID {UserId} was not found for deletion", id);
                return Error.NotFound("User", id);
            }

            users.Remove(entity);
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Deleted user {UserId}", id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError("Error deleting user {UserId} ({FailureType}).", id, ex.GetType().Name);
            return Error.BusinessRule("Cannot delete user due to existing dependencies");
        }
    }
    private IQueryable<User> QueryWithDetails(UserAccessScope? scope)
    {
        var query = users.Query().VisibleUsers(scope).Include(user => user.Team).AsNoTracking();
        return scope is { IsAdmin: false }
            ? query.Include(user => user.Tasks.Where(task => task.Project.TeamId == scope.TeamId))
            : query.Include(user => user.Tasks);
    }
}
