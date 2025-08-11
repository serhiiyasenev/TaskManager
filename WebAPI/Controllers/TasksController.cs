using System.Text.Json;
using BLL.Interfaces;
using BLL.Models.Tasks;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Task = DAL.Entities.Task;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController(ITasksService tasksService, IQueueService queueService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<Task>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<Task>>> GetAll()
    {
        return Ok(await tasksService.GetTasksAsync());
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Task), StatusCodes.Status200OK)]
    public async Task<ActionResult<Task>> GetById([FromRoute] int id)
    {
        return Ok(await tasksService.GetTaskByIdAsync(id));
    }

    [HttpPost]
    [ProducesResponseType(typeof(Task), StatusCodes.Status200OK)]
    public async Task<ActionResult<Task>> Add([FromBody] Task task)
    {
        return Ok(await tasksService.AddTaskAsync(task));
    }

    [HttpPost("AddExecutedTask")]
    [ProducesResponseType(typeof(ExecutedTaskResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<ExecutedTaskResult>> AddExecutedTask([FromBody] ExecutedTask executedTask)
    {
        var result = await tasksService.AddExecutedTaskAsync(executedTask);
        var json = JsonSerializer.Serialize(executedTask);
        var postedMessageToQueue = await queueService.PostValue(json);
        var executedTaskResult = new ExecutedTaskResult
        {
            Task = result,
            PostedMessageToQueueResult = postedMessageToQueue
        };
        return Ok(executedTaskResult);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(Task), StatusCodes.Status200OK)]
    public async Task<ActionResult<Task>> Update([FromRoute] int id, [FromBody] Task task)
    {
        return Ok(await tasksService.UpdateTaskByIdAsync(id, task));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<Task>> DeleteById([FromRoute] int id)
    {
        await tasksService.DeleteTaskByIdAsync(id);
        return NoContent();
    }
}