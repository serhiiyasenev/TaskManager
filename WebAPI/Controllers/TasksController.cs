using System.Text.Json;
using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Tasks;
using BLL.Models.Messaging;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using WebAPI.Authentication;
using Task = DAL.Entities.Task;

namespace WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TasksController(ITasksService tasksService, IQueueService queueService) : ControllerBase
{
    [HttpGet("page")]
    [ProducesResponseType(typeof(TaskPageResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TaskPageResult>> GetPage([FromQuery] TaskPageQuery query, CancellationToken ct)
    {
        var result = await tasksService.GetTaskPageAsync(query, ct);
        return result.ToActionResult();
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<TaskDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<TaskDetailDto>>> GetAll(CancellationToken ct)
    {
        var result = await tasksService.GetTasksAsync(ct);
        return result.ToActionResult();
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TaskDetailDto>> GetById([FromRoute] int id, CancellationToken ct)
    {
        var result = await tasksService.GetTaskByIdAsync(id, ct);
        return result.ToActionResult();
    }

    [HttpPost]
    [ProducesResponseType(typeof(TaskWriteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public System.Threading.Tasks.Task<ActionResult<Task>> Add([FromBody] TaskWriteRequest request, CancellationToken ct) => Add(request.ToEntity(), ct);

    [NonAction]
    public async Task<ActionResult<Task>> Add(Task task, CancellationToken ct)
    {
        var result = await tasksService.AddTaskAsync(TaskWriteRequest.Sanitize(task), ct);
        return result.IsFailure ? result.ToActionResult() : Ok(TaskWriteResponse.FromEntity(result.Value!));
    }

    [HttpPost("AddExecutedTask")]
    [ProducesResponseType(typeof(ExecutedTaskResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ExecutedTaskResult>> AddExecutedTask([FromBody] ExecutedTask executedTask, CancellationToken ct)
    {
        var result = await tasksService.AddExecutedTaskAsync(executedTask, ct);
        
        if (result.IsFailure)
            return Result<ExecutedTaskResult>.Failure(result.Error).ToActionResult();

        var envelope = new TaskNotificationEnvelope
        {
            Type = NotificationEventType.ExecutedTaskCompleted,
            ExecutedTask = new ExecutedTaskNotification(
                result.Value!.TaskId,
                result.Value!.TaskName,
                result.Value!.CreatedAt),
            CorrelationId = Guid.NewGuid().ToString("N")
        };

        var postedMessageToQueue = await queueService.PostValue(JsonSerializer.Serialize(envelope), null, ct);
        var executedTaskResult = new ExecutedTaskResult
        {
            Task = result.Value!,
            PostedMessageToQueueResult = postedMessageToQueue
        };
        return Ok(executedTaskResult);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(TaskWriteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async System.Threading.Tasks.Task<ActionResult<Task>> Update([FromRoute] int id, [FromBody] TaskWriteRequest request, CancellationToken ct)
    {
        var version = ExpectedVersion(request.ExpectedVersion);
        if (version.IsFailure) return VersionProblem(version.Error);
        var task = request.ToEntity();
        var result = version.Value.HasValue
            ? await tasksService.UpdateTaskWithVersionAsync(id, task, version.Value, ct)
            : await tasksService.UpdateTaskByIdAsync(id, task, ct);
        return result.IsFailure ? result.ToActionResult() : Ok(TaskWriteResponse.FromEntity(result.Value!));
    }

    [NonAction]
    public async Task<ActionResult<Task>> Update(int id, Task task, CancellationToken ct)
    {
        var result = await tasksService.UpdateTaskByIdAsync(id, TaskWriteRequest.Sanitize(task), ct);
        return result.IsFailure ? result.ToActionResult() : Ok(TaskWriteResponse.FromEntity(result.Value!));
    }

    [HttpPut("{id}/reminder")]
    [ProducesResponseType(typeof(TaskWriteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Task>> UpdateReminder([FromRoute] int id, [FromBody] UpdateTaskReminderDto reminderDto, CancellationToken ct)
    {
        var version = ExpectedVersion(reminderDto.ExpectedVersion);
        if (version.IsFailure) return VersionProblem(version.Error);
        var result = version.Value.HasValue
            ? await tasksService.UpdateTaskReminderWithVersionAsync(id, reminderDto, version.Value, ct)
            : await tasksService.UpdateTaskReminderAsync(id, reminderDto, ct);
        return result.IsFailure ? result.ToActionResult() : Ok(TaskWriteResponse.FromEntity(result.Value!));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteById([FromRoute] int id, CancellationToken ct)
    {
        var version = ExpectedVersion(null);
        if (version.IsFailure) return VersionProblem(version.Error);
        var result = version.Value.HasValue
            ? await tasksService.DeleteTaskWithVersionAsync(id, version.Value, ct)
            : await tasksService.DeleteTaskByIdAsync(id, ct);
        return result.ToActionResult();
    }

    private Result<Guid?> ExpectedVersion(Guid? bodyVersion)
    {
        if (bodyVersion == Guid.Empty) return Error.Validation("ExpectedVersion", "Use the task's current non-empty version.");
        Guid? headerVersion = null;
        if (HttpContext is not null && Request.Headers.TryGetValue("If-Match", out var values))
        {
            var header = values.Count == 1 ? values[0]?.Trim() : null;
            if (header is not { Length: 38 } || header[0] != '"' || header[^1] != '"'
                || !Guid.TryParseExact(header[1..^1], "D", out var parsed) || parsed == Guid.Empty)
                return Error.Validation("ExpectedVersion", "If-Match must contain one strong quoted task version.");
            headerVersion = parsed;
        }
        if (bodyVersion.HasValue && headerVersion.HasValue && bodyVersion != headerVersion)
            return Error.Validation("ExpectedVersion", "The request body and If-Match versions must agree.");
        var expected = bodyVersion ?? headerVersion;
        // A missing HTTP context occurs only in existing direct controller tests.
        // Every browser HTTP mutation requires a version; explicit JWT stays compatible.
        if (HttpContext is not null && !BrowserAuthenticationExtensions.HasBearerHeader(Request) && !expected.HasValue)
            return Error.Custom("Error.PreconditionRequired", "Load the task's current version before changing or deleting it.");
        return Result<Guid?>.Success(expected);
    }

    private ObjectResult VersionProblem(Error error)
    {
        var result = Problem(statusCode: error.Code == "Error.PreconditionRequired" ? StatusCodes.Status428PreconditionRequired : StatusCodes.Status400BadRequest,
            title: error.Code, detail: error.Message);
        ((ProblemDetails)result.Value!).Extensions["code"] = error.Code;
        return result;
    }
}
