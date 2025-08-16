using BLL.Exceptions;
using BLL.Interfaces;
using DAL.Entities;
using DAL.Enum;
using DAL.Repositories.Interfaces;
using Task = System.Threading.Tasks.Task;

namespace BLL.Services;

public class TasksService(
    IRepository<DAL.Entities.Task> tasks,
    IReadRepository<User> users,
    IReadRepository<Project> projects,
    IRepository<ExecutedTask> executedTasks,
    IUnitOfWork uow) : ITasksService
{
    public async Task<List<DAL.Entities.Task>> GetTasksAsync() => await tasks.ListAsync();

    public async Task<DAL.Entities.Task> GetTaskByIdAsync(int id) => await tasks.GetByIdAsync(id) ?? throw new NotFoundException(nameof(DAL.Entities.Task), id);

    public async Task<DAL.Entities.Task> AddTaskAsync(DAL.Entities.Task task)
    {
        if (!await users.AnyAsync(u => u.Id == task.PerformerId))
            throw new NotFoundException("PerformerId", task.PerformerId);
        if (!await projects.AnyAsync(p => p.Id == task.ProjectId))
            throw new NotFoundException("ProjectId", task.ProjectId);

        task.Id = 0;
        task.CreatedAt = DateTime.UtcNow;
        if (task.State is TaskState.ToDo or TaskState.InProgress) task.FinishedAt = null;

        await tasks.AddAsync(task);
        await uow.SaveChangesAsync();
        return task;
    }

    public async Task<ExecutedTask> AddExecutedTaskAsync(ExecutedTask executedTask)
    {
        executedTask.Id = 0;
        executedTask.CreatedAt = DateTime.UtcNow;
        await executedTasks.AddAsync(executedTask);
        await uow.SaveChangesAsync();
        return executedTask;
    }

    public async Task<DAL.Entities.Task> UpdateTaskByIdAsync(int id, DAL.Entities.Task task)
    {
        var entity = await tasks.GetByIdAsync(id) ?? throw new NotFoundException(nameof(DAL.Entities.Task), id);

        if (!await users.AnyAsync(u => u.Id == task.PerformerId))
            throw new NotFoundException("PerformerId", task.PerformerId);
        if (!await projects.AnyAsync(p => p.Id == task.ProjectId))
            throw new NotFoundException("ProjectId", task.ProjectId);

        entity.ProjectId = task.ProjectId;
        entity.PerformerId = task.PerformerId;
        entity.Name = task.Name;
        entity.Description = task.Description;
        entity.State = task.State;
        entity.FinishedAt = task.State is TaskState.Done or TaskState.Canceled ? DateTime.UtcNow : null;

        tasks.Update(entity);
        await uow.SaveChangesAsync();
        return entity;
    }

    public async Task DeleteTaskByIdAsync(int id)
    {
        var entity = await tasks.GetByIdAsync(id) ?? throw new NotFoundException(nameof(DAL.Entities.Task), id);
        try
        {
            tasks.Remove(entity);
            await uow.SaveChangesAsync();
        }
        catch
        {
            throw new CanNotDeleteException(nameof(DAL.Entities.Task), id);
        }
    }
}
