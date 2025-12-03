using AutoMapper;
using BLL.Mapping;
using BLL.Services;
using DAL.Entities;
using DAL.Enum;
using DAL.Repositories.Implementation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests.Integration;

public class TasksServiceIntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly Mock<ILogger<TasksService>> _logger = new();
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

    public System.Threading.Tasks.Task InitializeAsync()
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task DisposeAsync()
    {
        fixture.ResetDatabase();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private TasksService CreateService()
    {
        var taskRepo = new EfCoreRepository<DAL.Entities.Task>(fixture.Context);
        var userRepo = new EfCoreRepository<User>(fixture.Context);
        var projectRepo = new EfCoreRepository<Project>(fixture.Context);
        var executedTaskRepo = new EfCoreRepository<ExecutedTask>(fixture.Context);
        var uow = new UnitOfWork(fixture.Context);

        return new TasksService(taskRepo, userRepo, projectRepo, executedTaskRepo, uow, _mapper, _logger.Object);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTasksAsync_ReturnsAllTasks()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetTasksAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Count >= 2, $"Expected at least 2 tasks, got {result.Value.Count}");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTaskByIdAsync_ExistingId_ReturnsTask()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetTaskByIdAsync(1);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.Id);
        // Note: Name may have been updated by other tests, just verify task exists
        Assert.NotNull(result.Value.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTaskByIdAsync_NonExistingId_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetTaskByIdAsync(999);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Task with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddTaskAsync_ValidTask_CreatesTask()
    {
        // Arrange
        var service = CreateService();
        var newTask = new DAL.Entities.Task
        {
            Name = "New Integration Task",
            Description = "New task description",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.ToDo
        };

        // Act
        var result = await service.AddTaskAsync(newTask);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Id > 0);
        Assert.Equal("New Integration Task", result.Value.Name);
        Assert.True(result.Value.CreatedAt <= DateTime.UtcNow);
        Assert.Null(result.Value.FinishedAt); // ToDo state should have null FinishedAt

        // Verify it's in the database
        var getResult = await service.GetTaskByIdAsync(result.Value.Id);
        Assert.True(getResult.IsSuccess);
        Assert.Equal("New Integration Task", getResult.Value!.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddTaskAsync_InvalidPerformer_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();
        var newTask = new DAL.Entities.Task
        {
            Name = "Task",
            Description = "Description",
            ProjectId = 1,
            PerformerId = 999, // Non-existent performer
            State = TaskState.ToDo
        };

        // Act
        var result = await service.AddTaskAsync(newTask);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Performer with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddTaskAsync_InvalidProject_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();
        var newTask = new DAL.Entities.Task
        {
            Name = "Task",
            Description = "Description",
            ProjectId = 999, // Non-existent project
            PerformerId = 1,
            State = TaskState.ToDo
        };

        // Act
        var result = await service.AddTaskAsync(newTask);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Project with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateTaskByIdAsync_ExistingTask_UpdatesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var updatedTask = new DAL.Entities.Task
        {
            Name = "Updated Task 1",
            Description = "Updated description",
            ProjectId = 1,
            PerformerId = 2,
            State = TaskState.Done
        };

        // Act
        var result = await service.UpdateTaskByIdAsync(1, updatedTask);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.Id);
        Assert.Equal("Updated Task 1", result.Value.Name);
        Assert.Equal("Updated description", result.Value.Description);
        Assert.Equal(2, result.Value.PerformerId);
        Assert.Equal(TaskState.Done, result.Value.State);
        Assert.NotNull(result.Value.FinishedAt); // Done state should set FinishedAt

        // Verify persistence
        var getResult = await service.GetTaskByIdAsync(1);
        Assert.True(getResult.IsSuccess);
        Assert.Equal("Updated Task 1", getResult.Value!.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateTaskByIdAsync_NonExistingTask_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();
        var updatedTask = new DAL.Entities.Task
        {
            Name = "Task",
            Description = "Description",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.ToDo
        };

        // Act
        var result = await service.UpdateTaskByIdAsync(999, updatedTask);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Task with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteTaskByIdAsync_ExistingTask_DeletesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        
        // Create a task to delete
        var newTask = new DAL.Entities.Task
        {
            Name = "Task to Delete",
            Description = "Will be deleted",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.ToDo
        };
        var createResult = await service.AddTaskAsync(newTask);
        Assert.True(createResult.IsSuccess);
        var taskId = createResult.Value!.Id;

        // Act
        var result = await service.DeleteTaskByIdAsync(taskId);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify it's deleted
        var getResult = await service.GetTaskByIdAsync(taskId);
        Assert.True(getResult.IsFailure);
        Assert.Equal("Error.NotFound", getResult.Error.Code);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteTaskByIdAsync_NonExistingTask_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.DeleteTaskByIdAsync(999);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Task with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddExecutedTaskAsync_ValidTask_CreatesExecutedTask()
    {
        // Arrange
        var service = CreateService();
        var executedTask = new ExecutedTask
        {
            TaskId = 1,
            TaskName = "Executed Task 1"
        };

        // Act
        var result = await service.AddExecutedTaskAsync(executedTask);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Id > 0);
        Assert.Equal(1, result.Value.TaskId);
        Assert.Equal("Executed Task 1", result.Value.TaskName);
        Assert.True(result.Value.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateTaskByIdAsync_ChangeStateFromToDoToDone_SetsFinishedAt()
    {
        // Arrange
        var service = CreateService();
        var addResult = await service.AddTaskAsync(new DAL.Entities.Task
        {
            Name = "New Task",
            Description = "Test task",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.ToDo
        });
        var taskId = addResult.Value!.Id;

        var updateTask = new DAL.Entities.Task
        {
            Name = "New Task",
            Description = "Test task",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.Done
        };

        // Act
        var result = await service.UpdateTaskByIdAsync(taskId, updateTask);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(TaskState.Done, result.Value!.State);
        Assert.NotNull(result.Value!.FinishedAt);
        Assert.True(result.Value!.FinishedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateTaskByIdAsync_ChangeStateFromDoneToInProgress_ClearsFinishedAt()
    {
        // Arrange
        var service = CreateService();
        var addResult = await service.AddTaskAsync(new DAL.Entities.Task
        {
            Name = "Done Task",
            Description = "Test task",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.Done
        });
        var taskId = addResult.Value!.Id;

        var updateTask = new DAL.Entities.Task
        {
            Name = "Done Task",
            Description = "Test task",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.InProgress
        };

        // Act
        var result = await service.UpdateTaskByIdAsync(taskId, updateTask);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(TaskState.InProgress, result.Value!.State);
        Assert.Null(result.Value!.FinishedAt);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddTaskAsync_MultipleTasksToSameProject_AllCreatedSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var task1 = new DAL.Entities.Task
        {
            Name = "Task 1",
            Description = "First task",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.ToDo
        };
        var task2 = new DAL.Entities.Task
        {
            Name = "Task 2",
            Description = "Second task",
            ProjectId = 1,
            PerformerId = 2,
            State = TaskState.InProgress
        };

        // Act
        var result1 = await service.AddTaskAsync(task1);
        var result2 = await service.AddTaskAsync(task2);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.NotEqual(result1.Value!.Id, result2.Value!.Id);
        Assert.Equal(1, result1.Value!.ProjectId);
        Assert.Equal(1, result2.Value!.ProjectId);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTasksAsync_AfterAddingAndDeleting_ReturnsCorrectCount()
    {
        // Arrange
        var service = CreateService();
        var initialResult = await service.GetTasksAsync();
        var initialCount = initialResult.Value!.Count;

        // Add 2 tasks
        var addResult1 = await service.AddTaskAsync(new DAL.Entities.Task
        {
            Name = "Temp Task 1",
            Description = "Test",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.ToDo
        });
        
        await service.AddTaskAsync(new DAL.Entities.Task
        {
            Name = "Temp Task 2",
            Description = "Test",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.ToDo
        });

        // Delete one task
        await service.DeleteTaskByIdAsync(addResult1.Value!.Id);

        // Act
        var result = await service.GetTasksAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(initialCount + 1, result.Value!.Count);
    }
}
