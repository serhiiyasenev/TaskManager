#nullable enable annotations
using AutoMapper;
using BLL.Authorization;
using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Tasks;
using BLL.Configuration;
using DAL.Entities;
using DAL.Enum;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BLL.Services;

public class TasksService(
    IRepository<DAL.Entities.Task> tasks,
    IReadRepository<User> users,
    IReadRepository<Project> projects,
    IRepository<ExecutedTask> executedTasks,
    IUnitOfWork uow,
    IMapper mapper,
    ILogger<TasksService> logger,
    IOptions<ReminderOptions>? reminderOptions = null,
    IUserAccess? access = null)
    : ITasksService
{
    private readonly ReminderOptions _reminderOptions = reminderOptions?.Value ?? new ReminderOptions();

    public async Task<Result<List<TaskDetailDto>>> GetTasksAsync(CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            var taskList = await tasks.Query().VisibleTasks(scope)
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
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            logger.LogInformation("Retrieving task {TaskId}", id);
            var task = await tasks.Query().VisibleTasks(scope)
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
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            if (scope is not null) task = TaskWriteRequest.Sanitize(task);
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

            var assignmentError = await ValidateAssignmentAsync(task, scope, ct);
            if (assignmentError != Error.None) return assignmentError;
            task.Id = 0;
            task.CreatedAt = DateTime.UtcNow;
            ApplyReminderDefaults(task);
            if (task.State is TaskState.ToDo or TaskState.InProgress) 
                task.FinishedAt = null;
            else if (scope is not null) task.FinishedAt = DateTime.UtcNow;

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
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            if (scope is not null)
            {
                var task = await tasks.GetByIdAsync(executedTask.TaskId, ct);
                if (task is null || !await CanAccessTaskAsync(task, scope, ct)) return Error.NotFound("Task", executedTask.TaskId);
                if (task.State != TaskState.Done) return Error.Validation("TaskId", "Only a completed task can be recorded as executed.");
                executedTask = new ExecutedTask { TaskId = task.Id, TaskName = task.Name };
            }
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
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            var entity = await tasks.GetByIdAsync(id, ct);
            if (entity is null || !await CanAccessTaskAsync(entity, scope, ct))
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

            var assignmentError = await ValidateAssignmentAsync(task, scope, ct);
            if (assignmentError != Error.None) return assignmentError;
            var previousDueDate = entity.DueDate;
            var previousPerformerId = entity.PerformerId;
            var previousReminderEnabled = entity.ReminderEnabled;
            var previousReminderOffset = entity.ReminderOffsetMinutes;
            var previousEscalationEnabled = entity.EscalationEnabled;
            var previousEscalationDelay = entity.EscalationDelayMinutes;

            entity.ProjectId = task.ProjectId;
            entity.PerformerId = task.PerformerId;
            entity.Name = task.Name;
            entity.Description = task.Description;
            entity.State = task.State;
            entity.DueDate = task.DueDate;
            entity.ReminderEnabled = task.ReminderEnabled;
            entity.ReminderOffsetMinutes = task.ReminderOffsetMinutes;
            entity.EscalationEnabled = task.EscalationEnabled;
            entity.EscalationDelayMinutes = task.EscalationDelayMinutes;
            if (!entity.ReminderEnabled
                || previousPerformerId != entity.PerformerId
                || previousDueDate != entity.DueDate
                || previousReminderOffset != entity.ReminderOffsetMinutes
                || previousReminderEnabled != entity.ReminderEnabled)
            {
                entity.ReminderSentAt = null;
                entity.ReminderNotificationId = null;
                if (!entity.ReminderEnabled)
                {
                    entity.ReminderOffsetMinutes = null;
                }
            }

            if (!entity.EscalationEnabled
                || previousPerformerId != entity.PerformerId
                || previousDueDate != entity.DueDate
                || previousEscalationDelay != entity.EscalationDelayMinutes
                || previousEscalationEnabled != entity.EscalationEnabled)
            {
                entity.EscalationSentAt = null;
                entity.EscalationNotificationId = null;
                if (!entity.EscalationEnabled)
                {
                    entity.EscalationDelayMinutes = null;
                }
            }

            if (entity.State is TaskState.Done or TaskState.Canceled)
            {
                entity.ReminderNotificationId = null;
                entity.EscalationNotificationId = null;
            }

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

    public async Task<Result<DAL.Entities.Task>> UpdateTaskReminderAsync(int id, UpdateTaskReminderDto reminderDto, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            var entity = await tasks.GetByIdAsync(id, ct);
            if (entity is null || !await CanAccessTaskAsync(entity, scope, ct))
            {
                logger.LogWarning("Task {TaskId} not found for reminder update", id);
                return Error.NotFound(nameof(DAL.Entities.Task), id);
            }

            var previousDueDate = entity.DueDate;
            var previousReminderEnabled = entity.ReminderEnabled;
            var previousReminderOffset = entity.ReminderOffsetMinutes;
            var previousEscalationEnabled = entity.EscalationEnabled;
            var previousEscalationDelay = entity.EscalationDelayMinutes;

            var dueDate = reminderDto.DueDate ?? entity.DueDate;
            entity.DueDate = dueDate;
            entity.ReminderEnabled = reminderDto.ReminderEnabled && dueDate.HasValue;
            entity.ReminderOffsetMinutes = ResolveReminderOffset(entity.ReminderEnabled, reminderDto.ReminderOffsetMinutes);
            entity.EscalationEnabled = reminderDto.EscalationEnabled && dueDate.HasValue;
            entity.EscalationDelayMinutes = ResolveEscalationDelay(entity.EscalationEnabled, reminderDto.EscalationDelayMinutes);

            if (!entity.ReminderEnabled)
            {
                entity.ReminderSentAt = null;
                entity.ReminderNotificationId = null;
                entity.ReminderOffsetMinutes = null;
            }
            else if (!previousReminderEnabled
                     || previousDueDate != entity.DueDate
                     || previousReminderOffset != entity.ReminderOffsetMinutes)
            {
                entity.ReminderSentAt = null;
                entity.ReminderNotificationId = null;
            }

            if (!entity.EscalationEnabled)
            {
                entity.EscalationSentAt = null;
                entity.EscalationNotificationId = null;
                entity.EscalationDelayMinutes = null;
            }
            else if (!previousEscalationEnabled
                     || previousDueDate != entity.DueDate
                     || previousEscalationDelay != entity.EscalationDelayMinutes)
            {
                entity.EscalationSentAt = null;
                entity.EscalationNotificationId = null;
            }

            tasks.Update(entity);
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Task reminders updated: {TaskId}", id);
            return Result<DAL.Entities.Task>.Success(entity);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating reminders for task {TaskId}", id);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result> DeleteTaskByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            var entity = await tasks.GetByIdAsync(id, ct);
            if (entity is null || !await CanAccessTaskAsync(entity, scope, ct))
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

    private async Task<bool> CanAccessTaskAsync(DAL.Entities.Task task, UserAccessScope? scope, CancellationToken ct) =>
        scope is null || scope.IsAdmin || (scope.TeamId.HasValue
            && await projects.AnyAsync(project => project.Id == task.ProjectId && project.TeamId == scope.TeamId.Value, ct));

    private async Task<Error> ValidateAssignmentAsync(DAL.Entities.Task task, UserAccessScope? scope, CancellationToken ct)
    {
        if (scope is null) return Error.None;
        var project = await projects.Query().VisibleProjects(scope).FirstOrDefaultAsync(project => project.Id == task.ProjectId, ct);
        if (project is null) return Error.NotFound("Project", task.ProjectId);
        return await users.AnyAsync(user => user.Id == task.PerformerId && user.TeamId == project.TeamId, ct)
            ? Error.None : Error.Validation("PerformerId", "The performer must be a current member of the project's team.");
    }

    private void ApplyReminderDefaults(DAL.Entities.Task task)
    {
        task.ReminderEnabled = task.ReminderEnabled && task.DueDate.HasValue;
        task.EscalationEnabled = task.EscalationEnabled && task.DueDate.HasValue;
        task.ReminderOffsetMinutes = ResolveReminderOffset(task.ReminderEnabled, task.ReminderOffsetMinutes);
        task.EscalationDelayMinutes = ResolveEscalationDelay(task.EscalationEnabled, task.EscalationDelayMinutes);
        if (!task.ReminderEnabled)
        {
            task.ReminderSentAt = null;
        }
        if (!task.EscalationEnabled)
        {
            task.EscalationSentAt = null;
        }
    }

    private int? ResolveReminderOffset(bool enabled, int? providedOffset)
    {
        return enabled
            ? providedOffset ?? _reminderOptions.DefaultReminderOffsetMinutes
            : null;
    }

    private int? ResolveEscalationDelay(bool enabled, int? providedDelay)
    {
        return enabled
            ? providedDelay ?? _reminderOptions.DefaultEscalationDelayMinutes
            : null;
    }
}

