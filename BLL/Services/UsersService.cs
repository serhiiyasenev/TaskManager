using AutoMapper;
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
    ILogger<UsersService> logger) : IUsersService
{
    public async Task<Result<List<UserDetailDto>>> GetUsersAsync(CancellationToken ct = default)
    {
        try
        {
            var entities = await users.Query()
                .Include(u => u.Team)
                .Include(u => u.Tasks)
                .AsNoTracking()
                .ToListAsync(ct);
            
            var dtos = mapper.Map<List<UserDetailDto>>(entities);
            logger.LogInformation("Retrieved {Count} users", dtos.Count);
            return Result<List<UserDetailDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving users");
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<UserDetailDto>> GetUserByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var user = await users.Query()
                .Include(u => u.Team)
                .Include(u => u.Tasks)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id, ct);
            
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
            logger.LogError(ex, "Error retrieving user {UserId}", id);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<UserDto>> UpdateUserByIdAsync(int id, UpdateUserDto userDto, CancellationToken ct = default)
    {
        try
        {
            var entity = await users.GetByIdAsync(id, ct);
            if (entity == null)
            {
                logger.LogWarning("User with ID {UserId} was not found for update", id);
                return Error.NotFound("User", id);
            }

            if (userDto.TeamId.HasValue && !await teams.AnyAsync(t => t.Id == userDto.TeamId.Value, ct))
            {
                logger.LogWarning("Team with ID {TeamId} was not found", userDto.TeamId.Value);
                return Error.NotFound("Team", userDto.TeamId.Value);
            }

            entity.TeamId = userDto.TeamId;
            entity.FirstName = userDto.FirstName;
            entity.LastName = userDto.LastName;
            entity.Email = userDto.Email;
            entity.BirthDay = userDto.BirthDay;

            users.Update(entity);
            await uow.SaveChangesAsync(ct);

            var dto = new UserDto(entity.Id, entity.TeamId, entity.FirstName, entity.LastName, entity.Email, entity.RegisteredAt, entity.BirthDay);
            logger.LogInformation("Updated user {UserId}", id);
            return Result<UserDto>.Success(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating user {UserId}", id);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result> DeleteUserByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
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
            logger.LogError(ex, "Error deleting user {UserId}", id);
            return Error.BusinessRule("Cannot delete user due to existing dependencies");
        }
    }
}