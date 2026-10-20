#nullable enable annotations
using System.Data;
using AutoMapper;
using BLL.Authorization;
using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Tasks;
using BLL.Models.Messaging;
using System.Text.Json;
using BLL.Configuration;
using DAL.Entities;
using DAL.Context;
using DAL.Enum;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
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
    IUserAccess? access = null,
    TaskContext? context = null,
    IOptions<RabbitMqOptions>? rabbitOptions = null)
    : ITasksService
{
    private readonly ReminderOptions _reminderOptions = reminderOptions?.Value ?? new ReminderOptions();

    public async Task<Result<TaskPageResult>> GetTaskPageAsync(TaskPageQuery request, CancellationToken ct = default)
    {
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            if (request.Page < 1 || request.PageSize is < 1 or > 100
                || (long)(request.Page - 1) * request.PageSize > int.MaxValue)
                return Error.Validation("Page", "Use a positive page and a page size between 1 and 100.");
            if (request.ProjectId is <= 0 || request.PerformerId is <= 0
                || (request.State.HasValue && !Enum.IsDefined(request.State.Value)))
                return Error.Validation("Filters", "The project, performer or state filter is invalid.");
            if (request.Sort is not ("due" or "name" or "created") || request.Direction is not ("asc" or "desc"))
                return Error.Validation("Sort", "Use due, name or created with asc or desc.");
            var search = request.Search?.Trim() ?? "";
            if (search.Length > 100) return Error.Validation("Search", "Search is limited to 100 characters.");
            if (request.DueFrom.HasValue && request.DueTo.HasValue && request.DueFrom.Value > request.DueTo.Value)
                return Error.Validation("DueTo", "The due window must end after it starts.");

            // Apply authorization in SQL before every optional filter, count and
            // page. A foreign project/performer produces an empty scoped result.
            var query = tasks.Query().VisibleTasks(scope);
            if (request.ProjectId.HasValue) query = query.Where(task => task.ProjectId == request.ProjectId.Value);
            if (request.PerformerId.HasValue) query = query.Where(task => task.PerformerId == request.PerformerId.Value);
            if (request.State.HasValue) query = query.Where(task => task.State == request.State.Value);
            if (search.Length > 0)
            {
                var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
                query = query.Where(task => EF.Functions.Like(task.Name, pattern, "\\") || EF.Functions.Like(task.Description, pattern, "\\"));
            }
            if (request.DueFrom.HasValue)
            {
                var from = request.DueFrom.Value.UtcDateTime;
                query = query.Where(task => task.DueDate >= from);
            }
            if (request.DueTo.HasValue)
            {
                var to = request.DueTo.Value.UtcDateTime;
                query = query.Where(task => task.DueDate <= to);
            }
            if (request.Overdue.HasValue)
            {
                var now = DateTime.UtcNow;
                query = request.Overdue.Value
                    ? query.Where(task => task.DueDate < now && task.State != TaskState.Done && task.State != TaskState.Canceled)
                    : query.Where(task => task.DueDate == null || task.DueDate >= now || task.State == TaskState.Done || task.State == TaskState.Canceled);
            }
            var total = await query.CountAsync(ct);
            var offset = (request.Page - 1) * request.PageSize;
            if (offset >= total) return Result<TaskPageResult>.Success(new([], request.Page, request.PageSize, total));
            var descending = request.Direction == "desc";
            IOrderedQueryable<DAL.Entities.Task> ordered = request.Sort switch
            {
                "due" => descending ? query.OrderBy(task => task.DueDate == null).ThenByDescending(task => task.DueDate)
                    : query.OrderBy(task => task.DueDate == null).ThenBy(task => task.DueDate),
                "name" => descending ? query.OrderByDescending(task => task.Name) : query.OrderBy(task => task.Name),
                _ => descending ? query.OrderByDescending(task => task.CreatedAt) : query.OrderBy(task => task.CreatedAt)
            };
            var page = await ordered.ThenBy(task => task.Id).Skip(offset).Take(request.PageSize)
                .Include(task => task.Performer).AsNoTracking().ToListAsync(ct);
            return Result<TaskPageResult>.Success(new(mapper.Map<List<TaskDetailDto>>(page), request.Page, request.PageSize, total));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Error retrieving a task page ({FailureType}).", ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

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
            logger.LogError("Error retrieving tasks ({FailureType}).", ex.GetType().Name);
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
            logger.LogError("Error retrieving task {TaskId} ({FailureType}).", id, ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public Task<Result<DAL.Entities.Task>> AddTaskAsync(DAL.Entities.Task task, CancellationToken ct = default) =>
        InTaskWriteAsync(() => AddTaskCoreAsync(task, ct), checkAssignee: true, ct);

    private async Task<Result<DAL.Entities.Task>> AddTaskCoreAsync(DAL.Entities.Task task, CancellationToken ct)
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

            logger.LogInformation("Task created: {TaskId}", task.Id);
            return Result<DAL.Entities.Task>.Success(task);
        }
        catch (Exception exception) when (access is not null && IsDeadlock(exception)) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Error creating task ({FailureType}).", ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<ExecutedTask>> AddExecutedTaskAsync(ExecutedTask executedTask, CancellationToken ct = default)
    {
        if (access is not null)
        {
            ExecutedTask? committed = null;
            var result = await InTaskWriteAsync(async () =>
            {
                var scope = await access.GetScopeAsync(ct);
                if (scope is null) return Error.Unauthorized;
                var task = await tasks.GetByIdAsync(executedTask.TaskId, ct);
                if (task is null || !await CanAccessTaskAsync(task, scope, ct)) return Error.NotFound("Task", executedTask.TaskId);
                if (task.State != TaskState.Done) return Error.Validation("TaskId", "Only a completed task can be recorded as executed.");
                var project = await projects.Query().VisibleProjects(scope).SingleOrDefaultAsync(value => value.Id == task.ProjectId, ct);
                if (project is null) return Error.NotFound("Task", task.Id);
                var id = Guid.NewGuid();
                var now = DateTime.UtcNow;
                committed = new ExecutedTask { TaskId = task.Id, TaskName = task.Name, CreatedAt = now, NotificationMessageId = id };
                var envelope = new TaskNotificationEnvelope
                {
                    Type = NotificationEventType.ExecutedTaskCompleted, CorrelationId = id.ToString("N"),
                    ExecutedTask = new(task.Id, task.Name, now, task.PerformerId, task.ProjectId, project.Name)
                };
                context!.NotificationOutbox.Add(new NotificationOutboxMessage
                {
                    Id = id, TaskId = task.Id, Kind = NotificationOutboxKind.ExecutedTaskCompleted,
                    QueueName = rabbitOptions?.Value.QueueName ?? "TestQueue", CreatedAtUtc = now,
                    Status = NotificationOutboxStatus.Pending, Payload = JsonSerializer.Serialize(envelope)
                });
                await executedTasks.AddAsync(committed, ct);
                await uow.SaveChangesAsync(ct);
                return Result<DAL.Entities.Task>.Success(task);
            }, checkAssignee: true, ct);
            return result.IsFailure ? Result<ExecutedTask>.Failure(result.Error) : Result<ExecutedTask>.Success(committed!);
        }
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
            logger.LogError("Error creating executed task ({FailureType}).", ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public Task<Result<DAL.Entities.Task>> UpdateTaskByIdAsync(int id, DAL.Entities.Task task, CancellationToken ct = default) =>
        InTaskWriteAsync(() => UpdateTaskCoreAsync(id, task, null, ct), checkAssignee: true, ct);

    public Task<Result<DAL.Entities.Task>> UpdateTaskWithVersionAsync(int id, DAL.Entities.Task task, Guid? expectedVersion, CancellationToken ct = default) =>
        InTaskWriteAsync(() => UpdateTaskCoreAsync(id, task, expectedVersion, ct), checkAssignee: true, ct);

    private async Task<Result<DAL.Entities.Task>> UpdateTaskCoreAsync(int id, DAL.Entities.Task task, Guid? expectedVersion, CancellationToken ct)
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

            if (expectedVersion.HasValue && expectedVersion.Value != entity.Version)
                return Error.Conflict("The task changed. Reload it before applying your edit.");

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

            logger.LogInformation("Task updated: {TaskId}", id);
            return Result<DAL.Entities.Task>.Success(entity);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("The task changed. Reload it before applying your edit.");
        }
        catch (Exception exception) when (access is not null && IsDeadlock(exception)) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Error updating task {TaskId} ({FailureType}).", id, ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public Task<Result<DAL.Entities.Task>> UpdateTaskStateWithVersionAsync(int id, TaskState state, Guid expectedVersion, CancellationToken ct = default) =>
        InTaskWriteAsync(() => UpdateTaskStateCoreAsync(id, state, expectedVersion, ct), checkAssignee: false, ct);

    private async Task<Result<DAL.Entities.Task>> UpdateTaskStateCoreAsync(int id, TaskState state, Guid expectedVersion, CancellationToken ct)
    {
        if (!Enum.IsDefined(state)) return Error.Validation("State", "Use a supported task state.");
        if (expectedVersion == Guid.Empty) return Error.Validation("ExpectedVersion", "Use the task's current non-empty version.");
        try
        {
            var scope = access is null ? null : await access.GetScopeAsync(ct);
            if (access is not null && scope is null) return Error.Unauthorized;
            var entity = await tasks.GetByIdAsync(id, ct);
            if (entity is null || !await CanAccessTaskAsync(entity, scope, ct)) return Error.NotFound(nameof(DAL.Entities.Task), id);
            if (expectedVersion != entity.Version) return Error.Conflict("The task changed. Reload it before applying your edit.");
            if (entity.State == state) return Result<DAL.Entities.Task>.Success(entity);

            // Existing assignment, source fields and reminder settings are not
            // re-submitted by the Board. Access is rechecked before committing.
            entity.State = state;
            entity.FinishedAt = state is TaskState.Done or TaskState.Canceled ? DateTime.UtcNow : null;
            if (state is TaskState.Done or TaskState.Canceled)
            {
                entity.ReminderNotificationId = null;
                entity.EscalationNotificationId = null;
            }
            tasks.Update(entity);
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Task state updated: {TaskId}", id);
            return Result<DAL.Entities.Task>.Success(entity);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("The task changed. Reload it before applying your edit.");
        }
    }

    public Task<Result<DAL.Entities.Task>> UpdateTaskReminderAsync(int id, UpdateTaskReminderDto reminderDto, CancellationToken ct = default) =>
        InTaskWriteAsync(() => UpdateTaskReminderCoreAsync(id, reminderDto, null, ct), checkAssignee: false, ct);

    public Task<Result<DAL.Entities.Task>> UpdateTaskReminderWithVersionAsync(int id, UpdateTaskReminderDto reminderDto, Guid? expectedVersion, CancellationToken ct = default) =>
        InTaskWriteAsync(() => UpdateTaskReminderCoreAsync(id, reminderDto, expectedVersion, ct), checkAssignee: false, ct);

    private async Task<Result<DAL.Entities.Task>> UpdateTaskReminderCoreAsync(int id, UpdateTaskReminderDto reminderDto, Guid? expectedVersion, CancellationToken ct)
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

            if (expectedVersion.HasValue && expectedVersion.Value != entity.Version)
                return Error.Conflict("The task changed. Reload it before applying reminder changes.");

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
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("The task changed. Reload it before applying reminder changes.");
        }
        catch (Exception exception) when (access is not null && IsDeadlock(exception)) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Error updating reminders for task {TaskId} ({FailureType}).", id, ex.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public Task<Result> DeleteTaskByIdAsync(int id, CancellationToken ct = default) => DeleteTaskWriteAsync(id, null, ct);

    public Task<Result> DeleteTaskWithVersionAsync(int id, Guid? expectedVersion, CancellationToken ct = default) => DeleteTaskWriteAsync(id, expectedVersion, ct);

    private async Task<Result> DeleteTaskWriteAsync(int id, Guid? expectedVersion, CancellationToken ct)
    {
        if (access is null) return await DeleteTaskCoreAsync(id, expectedVersion, ct);
        var result = await InTaskWriteAsync(async () =>
        {
            var scope = await access.GetScopeAsync(ct);
            if (scope is null) return Error.Unauthorized;
            // Retain only the scoped project context for the final delete guard.
            // A former assignee does not prevent cleaning up an obsolete task.
            var previous = await tasks.Query().VisibleTasks(scope).FirstOrDefaultAsync(task => task.Id == id, ct);
            if (previous is null) return Error.NotFound(nameof(DAL.Entities.Task), id);
            var deleted = await DeleteTaskCoreAsync(id, expectedVersion, ct);
            return deleted.IsSuccess ? Result<DAL.Entities.Task>.Success(previous) : Result<DAL.Entities.Task>.Failure(deleted.Error);
        }, checkAssignee: false, ct);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    private async Task<Result> DeleteTaskCoreAsync(int id, Guid? expectedVersion, CancellationToken ct)
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

            if (expectedVersion.HasValue && expectedVersion.Value != entity.Version)
                return Error.Conflict("The task changed. Reload it before deleting it.");

            tasks.Remove(entity);
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Task deleted: {TaskId}", id);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("The task changed. Reload it before deleting it.");
        }
        catch (Exception exception) when (access is not null && IsDeadlock(exception)) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Error deleting task {TaskId} ({FailureType}).", id, ex.GetType().Name);
            return Error.Custom("Error.CanNotDelete", $"Cannot delete Task with ID {id}. It may have dependencies.");
        }
    }

    private async Task<Result<DAL.Entities.Task>> InTaskWriteAsync(Func<Task<Result<DAL.Entities.Task>>> action, bool checkAssignee, CancellationToken ct)
    {
        // Existing direct repository tests use the legacy constructor. Real
        // caller-scoped services require the registered context and fail closed.
        if (access is null) return await action();
        if (context is null) return Error.UnexpectedError;
        const int attempts = 2;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            IDbContextTransaction? owned = null;
            var shared = context.Database.CurrentTransaction;
            try
            {
                if (shared is not null && shared.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                    return Error.Conflict("The task update could not be completed. Refresh and retry.");
                var transaction = shared ?? (owned = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct));
                var result = await action();
                if (result.IsFailure)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    context.ChangeTracker.Clear();
                    return result;
                }
                // SaveChanges has not committed this transaction. Fresh reads
                // here also catch command-boundary permission revocations and
                // roll back the task, version and any cascaded outbox changes.
                var current = await access.GetScopeAsync(ct);
                var project = current is null ? null : await projects.Query().VisibleProjects(current)
                    .FirstOrDefaultAsync(project => project.Id == result.Value!.ProjectId, ct);
                if (project is null || (checkAssignee && !await users.AnyAsync(user => user.Id == result.Value!.PerformerId
                    && user.TeamId == project.TeamId, ct)))
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    context.ChangeTracker.Clear();
                    return Error.Conflict("Your team access or the assignee changed. Refresh and retry.");
                }
                if (owned is not null)
                {
                    await transaction.CommitAsync(ct);
                    context.ChangeTracker.Clear();
                }
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (shared is not null) await shared.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception) when (IsDeadlock(exception))
            {
                context.ChangeTracker.Clear();
                logger.LogWarning("A task write encountered a deadlock (attempt {Attempt}).", attempt + 1);
                if (shared is not null || attempt + 1 == attempts)
                    return Error.Conflict("The task changed. Refresh and retry.");
            }
            catch (Exception exception)
            {
                if (shared is not null) await shared.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                logger.LogError("A task write failed ({ErrorType}).", exception.GetType().Name);
                return Error.UnexpectedError;
            }
            finally
            {
                if (owned is not null) await owned.DisposeAsync();
            }
        }
        return Error.Conflict("The task changed. Refresh and retry.");
    }

    private static bool IsDeadlock(Exception exception)
    {
        // EF can wrap a SQL deadlock in InvalidOperationException. Retry only
        // a typed server1205, including errors below the EF wrapper.
        Exception? current = exception;
        for (var depth = 0; current is not null && depth < 16; current = current.InnerException, depth++)
        {
            if (current is not SqlException sql) continue;
            foreach (SqlError error in sql.Errors)
                if (error.Number == 1205) return true;
        }
        return false;
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

