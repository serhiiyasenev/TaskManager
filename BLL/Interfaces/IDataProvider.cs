using DAL.Entities;
using Task = DAL.Entities.Task;

namespace BLL.Interfaces;

public interface IDataProvider
{
    Task<List<Project>> GetProjectsAsync();
    Task<Project> GetProjectByIdAsync(int id);
    Task<Project> AddProjectAsync(Project project);
    Task<Project> UpdateProjectByIdAsync(int id, Project project);
    Task<bool?> DeleteProjectByIdAsync(int id);

    Task<List<Task>> GetTasksAsync();
    Task<Task> GetTaskByIdAsync(int id);
    Task<Task> AddTaskAsync(Task task);
    Task<ExecutedTask> AddExecutedTaskAsync(ExecutedTask task);
    Task<Task> UpdateTaskByIdAsync(int id, Task task);
    Task<bool?> DeleteTaskByIdAsync(int id);

    Task<List<Team>> GetTeamsAsync();
    Task<Team> GetTeamByIdAsync(int id);
    Task<Team> AddTeamAsync(Team team);
    Task<Team> UpdateTeamByIdAsync(int id, Team team);
    Task<bool?> DeleteTeamByIdAsync(int id);

    Task<List<User>> GetUsersAsync();
    Task<User> GetUserByIdAsync(int id);
    Task<User> AddUserAsync(User user);
    Task<User> UpdateUserByIdAsync(int id, User user);
    Task<bool?> DeleteUserByIdAsync(int id);
}
