using BLL.Common;
using DAL.Entities;
using Task = DAL.Entities.Task;

namespace BLL.Interfaces;

public interface ITasksService
{
    System.Threading.Tasks.Task<Result<List<Task>>> GetTasksAsync(CancellationToken ct = default);
    System.Threading.Tasks.Task<Result<Task>> GetTaskByIdAsync(int id, CancellationToken ct = default);
    System.Threading.Tasks.Task<Result<Task>> AddTaskAsync(Task task, CancellationToken ct = default);
    System.Threading.Tasks.Task<Result<ExecutedTask>> AddExecutedTaskAsync(ExecutedTask task, CancellationToken ct = default);
    System.Threading.Tasks.Task<Result<Task>> UpdateTaskByIdAsync(int id, Task task, CancellationToken ct = default);
    System.Threading.Tasks.Task<Result> DeleteTaskByIdAsync(int id, CancellationToken ct = default);
}
