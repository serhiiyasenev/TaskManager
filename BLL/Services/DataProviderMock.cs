using BLL.Interfaces;
using DAL.Entities;
using Task = DAL.Entities.Task;

namespace BLL.Services;

public class DataProviderMock : IDataProvider
{
    private List<Project> projects;
    private List<Task> tasks;
    private List<Team> teams;
    private List<User> users;
    private List<ExecutedTask> executedTasks;

    public DataProviderMock()
    {
        projects = GenerateProjects();
        tasks = GenerateTasks();
        teams = GenerateTeams();
        users = GenerateUsers();
        executedTasks = [];
    }

    public async Task<List<Project>> GetProjectsAsync()
    {
        return await System.Threading.Tasks.Task.FromResult(projects);
    }

    public async Task<Project> GetProjectByIdAsync(int id)
    {
        var project = projects.FirstOrDefault(p => p.Id == id);
        return await System.Threading.Tasks.Task.FromResult(project);
    }

    public async Task<Project> AddProjectAsync(Project project)
    {
        var newId = projects.Max(p => p.Id) + 1;
        project.Id = newId;
        project.CreatedAt = DateTime.UtcNow;
        projects.Add(project);
        return await System.Threading.Tasks.Task.FromResult(project);
    }

    public async Task<Project> UpdateProjectByIdAsync(int id, Project project)
    {
        var existingProject = projects.FirstOrDefault(p => p.Id == id);
        if (existingProject != null)
        {
            project.Id = existingProject.Id;
            projects[projects.IndexOf(existingProject)] = project;
            return await System.Threading.Tasks.Task.FromResult(project);
        }
        return await System.Threading.Tasks.Task.FromResult<Project>(null);
    }

    public async Task<bool?> DeleteProjectByIdAsync(int id)
    {
        var project = projects.FirstOrDefault(p => p.Id == id);
        if (project != null) return await System.Threading.Tasks.Task.FromResult(projects.Remove(project));
        return await System.Threading.Tasks.Task.FromResult<bool?>(null);
    }

    public async Task<List<Task>> GetTasksAsync()
    {
        return await System.Threading.Tasks.Task.FromResult(tasks);
    }

    public async Task<Task> GetTaskByIdAsync(int id)
    {
        var task = tasks.FirstOrDefault(t => t.Id == id);
        return await System.Threading.Tasks.Task.FromResult(task);
    }

    public async Task<Task> AddTaskAsync(Task task)
    {
        var newId = tasks.Max(p => p.Id) + 1;
        task.Id = newId;
        task.CreatedAt = DateTime.UtcNow;
        tasks.Add(task);
        return await System.Threading.Tasks.Task.FromResult(task);
    }

    public async Task<ExecutedTask> AddExecutedTaskAsync(ExecutedTask executedTask)
    {
        var newId = tasks.Max(p => p.Id) + 1;
        executedTask.Id = newId;
        executedTask.CreatedAt = DateTime.UtcNow;
        executedTasks.Add(executedTask);
        return await System.Threading.Tasks.Task.FromResult(executedTask);
    }

    public async Task<Task> UpdateTaskByIdAsync(int id, Task task)
    {
        var existingTask = tasks.FirstOrDefault(t => t.Id == id);
        if (existingTask != null)
        {
            task.Id = existingTask.Id;
            tasks[tasks.IndexOf(existingTask)] = task;
            return await System.Threading.Tasks.Task.FromResult(task);
        }
        return await System.Threading.Tasks.Task.FromResult<Task>(null);
    }

    public async Task<bool?> DeleteTaskByIdAsync(int id)
    {
        var task = tasks.FirstOrDefault(p => p.Id == id);
        if (task != null) return await System.Threading.Tasks.Task.FromResult(tasks.Remove(task));
        return await System.Threading.Tasks.Task.FromResult<bool?>(null);
    }

    public async Task<List<Team>> GetTeamsAsync()
    {
        return await System.Threading.Tasks.Task.FromResult(teams);
    }

    public async Task<Team> GetTeamByIdAsync(int id)
    {
        var team = teams.FirstOrDefault(t => t.Id == id);
        return await System.Threading.Tasks.Task.FromResult(team);
    }

    public async Task<Team> AddTeamAsync(Team team)
    {
        var newId = teams.Max(p => p.Id) + 1;
        team.Id = newId;
        team.CreatedAt = DateTime.UtcNow;
        teams.Add(team);
        return await System.Threading.Tasks.Task.FromResult(team);
    }

    public async Task<Team> UpdateTeamByIdAsync(int id, Team team)
    {
        var existingTeam = teams.FirstOrDefault(t => t.Id == id);
        if (existingTeam != null)
        {
            team.Id = existingTeam.Id;
            teams[teams.IndexOf(existingTeam)] = team;
            return await System.Threading.Tasks.Task.FromResult(team);
        }
        return await System.Threading.Tasks.Task.FromResult<Team>(null);
    }

    public async Task<bool?> DeleteTeamByIdAsync(int id)
    {
        var team = teams.FirstOrDefault(p => p.Id == id);
        if (team != null) return await System.Threading.Tasks.Task.FromResult(teams.Remove(team));
        return await System.Threading.Tasks.Task.FromResult<bool?>(null);
    }

    public async Task<List<User>> GetUsersAsync()
    {
        return await System.Threading.Tasks.Task.FromResult(users);
    }

    public async Task<User> GetUserByIdAsync(int id)
    {
        var user = users.FirstOrDefault(u => u.Id == id);
        return await System.Threading.Tasks.Task.FromResult(user);
    }

    public async Task<User> AddUserAsync(User user)
    {
        var newId = users.Max(p => p.Id) + 1;
        user.Id = newId;
        user.RegisteredAt =  DateTime.UtcNow;
        users.Add(user);
        return await System.Threading.Tasks.Task.FromResult(user);
    }

    public async Task<User> UpdateUserByIdAsync(int id, User user)
    {
        var existingUser = users.FirstOrDefault(u => u.Id == id);
        if (existingUser != null)
        {
            user.Id = existingUser.Id;
            users[users.IndexOf(existingUser)] = user;
            return await System.Threading.Tasks.Task.FromResult(user);
        }
        return await System.Threading.Tasks.Task.FromResult<User>(null);
    }

    public async Task<bool?> DeleteUserByIdAsync(int id)
    {
        var user = users.FirstOrDefault(p => p.Id == id);
        if (user != null) return await System.Threading.Tasks.Task.FromResult(users.Remove(user));
        return await System.Threading.Tasks.Task.FromResult<bool?>(null);
    }

    private List<Project> GenerateProjects()
    {
        return
        [
            new Project(1, 1, 1, "Project 1", "Description 1", DateTime.UtcNow, DateTime.UtcNow.AddDays(10), null, null, null),
            new Project(2, 2, 2, "Project 2", "Description 2", DateTime.UtcNow, DateTime.UtcNow.AddDays(14), null,null, null),
            new Project(3, 3, 3, "Project 3", "Description 3", DateTime.UtcNow, DateTime.UtcNow.AddDays(21), null,null, null)
        ];
    }

    private List<Task> GenerateTasks()
    {
        return
        [
            new Task(1, 1, 1, "Task 1", "Description 1", TaskState.ToDo, DateTime.UtcNow, null, null, null),
            new Task(2, 2, 2, "Task 2", "Description 2", TaskState.Canceled, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow, null, null),
            new Task(3, 3, 3, "Task 3", "Description 3", TaskState.ToDo, DateTime.UtcNow, null, null, null),
            new Task(4, 1, 2, "Task 4", "Description 4", TaskState.InProgress, DateTime.UtcNow, null, null, null),
            new Task(5, 2, 3, "Task 5", "Description 5", TaskState.Done, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow, null, null)
        ];
    }

    private List<Team> GenerateTeams()
    {
        return
        [
            new Team(1, "Team 1", DateTime.UtcNow, null, null),
            new Team(2, "Team 2", DateTime.UtcNow, null, null),
            new Team(3, "Team 3", DateTime.UtcNow, null, null)
        ];
    }

    private List<User> GenerateUsers()
    {
        return
        [
            new User(1, 1, "John", "A", "john.A.doe@gmail.com", DateTime.Now, DateTime.Parse("1991-01-01"), null,null),
            new User(2, 2, "John", "B", "john.B.doe@gmail.com", DateTime.Now, DateTime.Parse("1992-02-02"), null,null),
            new User(3, 3, "John", "C", "john.C.doe@gmail.com", DateTime.Now, DateTime.Parse("1993-03-03"), null,null),
            new User(4, 1, "John", "D", "john.D.doe@gmail.com", DateTime.Now, DateTime.Parse("1994-04-04"), null,null),
            new User(5, 2, "John", "E", "john.E.doe@gmail.com", DateTime.Now, DateTime.Parse("1995-05-05"), null,null)
        ];
    }
}