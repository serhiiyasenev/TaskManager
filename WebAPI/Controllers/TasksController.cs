using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Tasks;
using BLL.Models.Messaging;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Task = DAL.Entities.Task;

namespace WebAPI.Controllers;

[ExcludeFromCodeCoverage]
[ApiController]
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
    [ProducesResponseType(typeof(Task), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Task>> Add([FromBody] Task task, CancellationToken ct)
    {
        var result = await tasksService.AddTaskAsync(task, ct);
        return result.ToActionResult();
    }

    [HttpPost("AddExecutedTask")]
    [ProducesResponseType(typeof(ExecutedTaskResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ExecutedTaskResult>> AddExecutedTask([FromBody] ExecutedTask executedTask, CancellationToken ct)
    {
        var result = await tasksService.AddExecutedTaskAsync(executedTask, ct);
        
        if (result.IsFailure)
            return StatusCode(500, result.Error);

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
    [ProducesResponseType(typeof(Task), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Task>> Update([FromRoute] int id, [FromBody] Task task, CancellationToken ct)
    {
        var result = await tasksService.UpdateTaskByIdAsync(id, task, ct);
        return result.ToActionResult();
    }

    [HttpPut("{id}/reminder")]
    [ProducesResponseType(typeof(Task), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Task>> UpdateReminder([FromRoute] int id, [FromBody] UpdateTaskReminderDto reminderDto, CancellationToken ct)
    {
        var result = await tasksService.UpdateTaskReminderAsync(id, reminderDto, ct);
        return result.ToActionResult();
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
