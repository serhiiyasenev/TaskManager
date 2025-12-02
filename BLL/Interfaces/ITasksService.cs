using BLL.Common;
using BLL.Models.Tasks;
using DAL.Entities;
using Task = DAL.Entities.Task;

namespace BLL.Interfaces;

public interface ITasksService
{
    System.Threading.Tasks.Task<Result<List<TaskDetailDto>>> GetTasksAsync(CancellationToken ct = default);
    System.Threading.Tasks.Task<Result<TaskDetailDto>> GetTaskByIdAsync(int id, CancellationToken ct = default);
    System.Threading.Tasks.Task<Result<Task>> AddTaskAsync(Task task, CancellationToken ct = default);
    System.Threading.Tasks.Task<Result<ExecutedTask>> AddExecutedTaskAsync(ExecutedTask task, CancellationToken ct = default);
    System.Threading.Tasks.Task<Result<Task>> UpdateTaskByIdAsync(int id, Task task, CancellationToken ct = default);
    System.Threading.Tasks.Task<Result> DeleteTaskByIdAsync(int id, CancellationToken ct = default);
}
