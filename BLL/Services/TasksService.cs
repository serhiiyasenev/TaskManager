using BLL.Exceptions;
using BLL.Interfaces;
using DAL.Entities;
using Task = System.Threading.Tasks.Task;

namespace BLL.Services;

public class TasksService(IDataProvider dataProvider) : ITasksService
{
    public async Task<List<DAL.Entities.Task>> GetTasksAsync()
    {
        return await dataProvider.GetTasksAsync();
    }

    public async Task<DAL.Entities.Task> GetTaskByIdAsync(int id)
    {
        return await dataProvider.GetTaskByIdAsync(id) ?? throw new NotFoundException(nameof(DAL.Entities.Task), id);
    }

    public async Task<DAL.Entities.Task> AddTaskAsync(DAL.Entities.Task task)
    {
        return await dataProvider.AddTaskAsync(task);
    }

    public async Task<ExecutedTask> AddExecutedTaskAsync(ExecutedTask task)
    {
        return await dataProvider.AddExecutedTaskAsync(task);
    }

    public async Task<DAL.Entities.Task> UpdateTaskByIdAsync(int id, DAL.Entities.Task task)
    {
        return await dataProvider.UpdateTaskByIdAsync(id, task) ?? throw new NotFoundException(nameof(DAL.Entities.Task), id);
    }

    public async Task DeleteTaskByIdAsync(int id)
    {
        var isDeleted = await dataProvider.DeleteTaskByIdAsync(id);
        switch (isDeleted)
        {
            case null:
                throw new NotFoundException(nameof(DAL.Entities.Task), id);
            case false:
                throw new CanNotDeleteException(nameof(DAL.Entities.Task), id);
        }
    }
}