using BLL.Common;
using BLL.Interfaces;
using DAL.Entities;
using DAL.Enum;
using DAL.Repositories.Interfaces;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public class TasksService : ITasksService
{
    private readonly IRepository<DAL.Entities.Task> _tasks;
    private readonly IReadRepository<User> _users;
    private readonly IReadRepository<Project> _projects;
    private readonly IRepository<ExecutedTask> _executedTasks;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<TasksService> _logger;

    public TasksService(
        IRepository<DAL.Entities.Task> tasks,
        IReadRepository<User> users,
        IReadRepository<Project> projects,
        IRepository<ExecutedTask> executedTasks,
        IUnitOfWork uow,
        ILogger<TasksService> logger)
    {
        _tasks = tasks;
        _users = users;
        _projects = projects;
        _executedTasks = executedTasks;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<List<DAL.Entities.Task>>> GetTasksAsync(CancellationToken ct = default)
    {
        try
        {
            var taskList = await _tasks.ListAsync(null, ct);
            _logger.LogInformation("Retrieved {Count} tasks", taskList.Count);
            return Result<List<DAL.Entities.Task>>.Success(taskList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tasks");
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<DAL.Entities.Task>> GetTaskByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Retrieving task {TaskId}", id);
            var task = await _tasks.GetByIdAsync(id, ct);

            if (task is null)
            {
                _logger.LogWarning("Task {TaskId} not found", id);
                return Error.NotFound(nameof(DAL.Entities.Task), id);
            }

            return Result<DAL.Entities.Task>.Success(task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving task {TaskId}", id);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<DAL.Entities.Task>> AddTaskAsync(DAL.Entities.Task task, CancellationToken ct = default)
    {
        try
        {
            // Validate performer exists
            if (!await _users.AnyAsync(u => u.Id == task.PerformerId, ct))
            {
                _logger.LogWarning("Performer {PerformerId} not found", task.PerformerId);
                return Error.NotFound("Performer", task.PerformerId);
            }

            // Validate project exists
            if (!await _projects.AnyAsync(p => p.Id == task.ProjectId, ct))
            {
                _logger.LogWarning("Project {ProjectId} not found", task.ProjectId);
                return Error.NotFound("Project", task.ProjectId);
            }

            task.Id = 0;
            task.CreatedAt = DateTime.UtcNow;
            if (task.State is TaskState.ToDo or TaskState.InProgress) 
                task.FinishedAt = null;

            await _tasks.AddAsync(task, ct);
            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation("Task created: {TaskId} - {TaskName}", task.Id, task.Name);
            return Result<DAL.Entities.Task>.Success(task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating task {TaskName}", task.Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<ExecutedTask>> AddExecutedTaskAsync(ExecutedTask executedTask, CancellationToken ct = default)
    {
        try
        {
            executedTask.Id = 0;
            executedTask.CreatedAt = DateTime.UtcNow;
            
            await _executedTasks.AddAsync(executedTask, ct);
            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation("ExecutedTask created: {ExecutedTaskId}", executedTask.Id);
            return Result<ExecutedTask>.Success(executedTask);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating executed task");
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<DAL.Entities.Task>> UpdateTaskByIdAsync(int id, DAL.Entities.Task task, CancellationToken ct = default)
    {
        try
        {
            var entity = await _tasks.GetByIdAsync(id, ct);
            if (entity is null)
            {
                _logger.LogWarning("Task {TaskId} not found for update", id);
                return Error.NotFound(nameof(DAL.Entities.Task), id);
            }

            // Validate performer exists
            if (!await _users.AnyAsync(u => u.Id == task.PerformerId, ct))
            {
                _logger.LogWarning("Performer {PerformerId} not found", task.PerformerId);
                return Error.NotFound("Performer", task.PerformerId);
            }

            // Validate project exists
            if (!await _projects.AnyAsync(p => p.Id == task.ProjectId, ct))
            {
                _logger.LogWarning("Project {ProjectId} not found", task.ProjectId);
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

            _tasks.Update(entity);
            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation("Task updated: {TaskId} - {TaskName}", id, entity.Name);
            return Result<DAL.Entities.Task>.Success(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating task {TaskId}", id);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result> DeleteTaskByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var entity = await _tasks.GetByIdAsync(id, ct);
            if (entity is null)
            {
                _logger.LogWarning("Task {TaskId} not found for deletion", id);
                return Error.NotFound(nameof(DAL.Entities.Task), id);
            }

            _tasks.Remove(entity);
            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation("Task deleted: {TaskId}", id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting task {TaskId}", id);
            return Error.Custom("Error.CanNotDelete", $"Cannot delete Task with ID {id}. It may have dependencies.");
        }
    }
}
