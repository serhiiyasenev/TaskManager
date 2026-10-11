using System.Text.Json;
using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Tasks;
using BLL.Models.Messaging;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Task = DAL.Entities.Task;

namespace WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TasksController(ITasksService tasksService, IQueueService queueService) : ControllerBase
{
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
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public System.Threading.Tasks.Task<ActionResult<Task>> Update([FromRoute] int id, [FromBody] TaskWriteRequest request, CancellationToken ct) => Update(id, request.ToEntity(), ct);

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
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Task>> UpdateReminder([FromRoute] int id, [FromBody] UpdateTaskReminderDto reminderDto, CancellationToken ct)
    {
        var result = await tasksService.UpdateTaskReminderAsync(id, reminderDto, ct);
        return result.IsFailure ? result.ToActionResult() : Ok(TaskWriteResponse.FromEntity(result.Value!));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteById([FromRoute] int id, CancellationToken ct)
    {
        var result = await tasksService.DeleteTaskByIdAsync(id, ct);
        return result.ToActionResult();
    }
}
