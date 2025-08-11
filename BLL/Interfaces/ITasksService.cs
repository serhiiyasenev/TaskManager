using DAL.Entities;
using Task = DAL.Entities.Task;

namespace BLL.Interfaces;

public interface ITasksService
{
    Task<List<Task>> GetTasksAsync();
    Task<Task> GetTaskByIdAsync(int id);
    Task<Task> AddTaskAsync(Task task);
    Task<ExecutedTask> AddExecutedTaskAsync(ExecutedTask task);
    Task<Task> UpdateTaskByIdAsync(int id, Task task);
    System.Threading.Tasks.Task DeleteTaskByIdAsync(int id);
}
