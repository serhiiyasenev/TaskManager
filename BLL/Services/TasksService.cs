using AutoMapper;
using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Tasks;
using DAL.Entities;
using DAL.Enum;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public class TasksService(
    IRepository<DAL.Entities.Task> tasks,
    IReadRepository<User> users,
    IReadRepository<Project> projects,
    IRepository<ExecutedTask> executedTasks,
    IUnitOfWork uow,
    IMapper mapper,
    ILogger<TasksService> logger)
    : ITasksService
{
    public async Task<Result<List<TaskDetailDto>>> GetTasksAsync(CancellationToken ct = default)
    {
        try
        {
            var taskList = await tasks.Query()
                .Include(t => t.Performer)
                .AsNoTracking()
                .ToListAsync(ct);
            
            var dtos = mapper.Map<List<TaskDetailDto>>(taskList);
            logger.LogInformation("Retrieved {Count} tasks", dtos.Count);
            return Result<List<TaskDetailDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving tasks");
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<TaskDetailDto>> GetTaskByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            logger.LogInformation("Retrieving task {TaskId}", id);
            var task = await tasks.Query()
                .Include(t => t.Performer)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (task is null)
            {
                logger.LogWarning("Task {TaskId} not found", id);
                return Error.NotFound(nameof(DAL.Entities.Task), id);
            }

            var dto = mapper.Map<TaskDetailDto>(task);
            return Result<TaskDetailDto>.Success(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving task {TaskId}", id);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<DAL.Entities.Task>> AddTaskAsync(DAL.Entities.Task task, CancellationToken ct = default)
    {
        try
        {
            // Validate performer exists
            if (!await users.AnyAsync(u => u.Id == task.PerformerId, ct))
            {
                logger.LogWarning("Performer {PerformerId} not found", task.PerformerId);
                return Error.NotFound("Performer", task.PerformerId);
            }

            // Validate project exists
            if (!await projects.AnyAsync(p => p.Id == task.ProjectId, ct))
            {
                logger.LogWarning("Project {ProjectId} not found", task.ProjectId);
                return Error.NotFound("Project", task.ProjectId);
            }

            task.Id = 0;
            task.CreatedAt = DateTime.UtcNow;
            if (task.State is TaskState.ToDo or TaskState.InProgress) 
                task.FinishedAt = null;

            await tasks.AddAsync(task, ct);
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Task created: {TaskId} - {TaskName}", task.Id, task.Name);
            return Result<DAL.Entities.Task>.Success(task);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating task {TaskName}", task.Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<ExecutedTask>> AddExecutedTaskAsync(ExecutedTask executedTask, CancellationToken ct = default)
    {
        try
        {
            executedTask.Id = 0;
            executedTask.CreatedAt = DateTime.UtcNow;
            
            await executedTasks.AddAsync(executedTask, ct);
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("ExecutedTask created: {ExecutedTaskId}", executedTask.Id);
            return Result<ExecutedTask>.Success(executedTask);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating executed task");
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<DAL.Entities.Task>> UpdateTaskByIdAsync(int id, DAL.Entities.Task task, CancellationToken ct = default)
    {
        try
        {
            var entity = await tasks.GetByIdAsync(id, ct);
            if (entity is null)
            {
                logger.LogWarning("Task {TaskId} not found for update", id);
                return Error.NotFound(nameof(DAL.Entities.Task), id);
            }

            // Validate performer exists
            if (!await users.AnyAsync(u => u.Id == task.PerformerId, ct))
            {
                logger.LogWarning("Performer {PerformerId} not found", task.PerformerId);
                return Error.NotFound("Performer", task.PerformerId);
            }

            // Validate project exists
            if (!await projects.AnyAsync(p => p.Id == task.ProjectId, ct))
            {
                logger.LogWarning("Project {ProjectId} not found", task.ProjectId);
                return Error.NotFound("Project", task.ProjectId);
            }

            entity.ProjectId = task.ProjectId;
            entity.PerformerId = task.PerformerId;
            entity.Name = task.Name;
            entity.Description = task.Description;
            entity.State = task.State;

            entity.FinishedAt = entity.State is TaskState.Done or TaskState.Canceled
                ? DateTime.UtcNow
                : null;

            tasks.Update(entity);
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Task updated: {TaskId} - {TaskName}", id, entity.Name);
            return Result<DAL.Entities.Task>.Success(entity);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating task {TaskId}", id);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result> DeleteTaskByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var entity = await tasks.GetByIdAsync(id, ct);
            if (entity is null)
            {
                logger.LogWarning("Task {TaskId} not found for deletion", id);
                return Error.NotFound(nameof(DAL.Entities.Task), id);
            }

            tasks.Remove(entity);
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Task deleted: {TaskId}", id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting task {TaskId}", id);
            return Error.Custom("Error.CanNotDelete", $"Cannot delete Task with ID {id}. It may have dependencies.");
        }
    }
}
