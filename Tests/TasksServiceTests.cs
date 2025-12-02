using BLL.Services;
using DAL.Entities;
using DAL.Enum;
using DAL.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq.Expressions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests;

public class TasksServiceTests
{
    private readonly Mock<IRepository<DAL.Entities.Task>> _tasks = new();
    private readonly Mock<IReadRepository<User>> _users = new();
    private readonly Mock<IReadRepository<Project>> _projects = new();
    private readonly Mock<IRepository<ExecutedTask>> _executedTasks = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ILogger<TasksService>> _logger = new();

    private TasksService CreateSut() => new(
        _tasks.Object,
        _users.Object,
        _projects.Object,
        _executedTasks.Object,
        _uow.Object,
        _logger.Object);

    [Fact]
    public async Task GetTasksAsync_ReturnsAllTasks()
    {
        // Arrange
        var data = new List<DAL.Entities.Task>
        {
            new() { Id = 1, Name = "Task1", Description = "D1", ProjectId = 1, PerformerId = 1, State = TaskState.ToDo },
            new() { Id = 2, Name = "Task2", Description = "D2", ProjectId = 1, PerformerId = 2, State = TaskState.InProgress }
        };

        _tasks.Setup(r => r.ListAsync(It.IsAny<Expression<Func<DAL.Entities.Task, bool>>?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(data);

        var sut = CreateSut();

        // Act
        var result = await sut.GetTasksAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Collection(result.Value!, 
            t => Assert.Equal(1, t.Id), 
            t => Assert.Equal(2, t.Id));
    }

    [Fact]
    public async Task GetTaskByIdAsync_Found_ReturnsTask()
    {
        // Arrange
        var task = new DAL.Entities.Task 
        { 
            Id = 5, 
            Name = "Task5", 
            Description = "D5", 
            ProjectId = 1, 
            PerformerId = 1, 
            State = TaskState.ToDo 
        };
        _tasks.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
              .ReturnsAsync(task);

        var sut = CreateSut();

        // Act
        var result = await sut.GetTaskByIdAsync(5);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(task, result.Value);
    }

    [Fact]
    public async Task GetTaskByIdAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        _tasks.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
              .ReturnsAsync((DAL.Entities.Task?)null);

        var sut = CreateSut();

        // Act
        var result = await sut.GetTaskByIdAsync(99);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task AddTaskAsync_ValidTask_CreatesTask()
    {
        // Arrange
        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        _projects.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Project, bool>>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var task = new DAL.Entities.Task
        {
            Id = 123,
            Name = "NewTask",
            Description = "Description",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.ToDo
        };

        var sut = CreateSut();
        var before = DateTime.UtcNow;

        // Act
        var result = await sut.AddTaskAsync(task);
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(task, result.Value);
        Assert.Equal(0, result.Value!.Id);
        Assert.True(result.Value!.CreatedAt >= before && result.Value!.CreatedAt <= after);
        Assert.Null(result.Value!.FinishedAt); // ToDo state should have null FinishedAt

        _tasks.Verify(r => r.AddAsync(task, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddTaskAsync_MissingPerformer_ReturnsFailure()
    {
        // Arrange
        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        var task = new DAL.Entities.Task
        {
            Name = "Task",
            Description = "D",
            ProjectId = 1,
            PerformerId = 999,
            State = TaskState.ToDo
        };

        var sut = CreateSut();

        // Act
        var result = await sut.AddTaskAsync(task);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Performer", result.Error.Message);
        _tasks.Verify(r => r.AddAsync(It.IsAny<DAL.Entities.Task>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddTaskAsync_MissingProject_ReturnsFailure()
    {
        // Arrange
        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        _projects.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Project, bool>>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

        var task = new DAL.Entities.Task
        {
            Name = "Task",
            Description = "D",
            ProjectId = 999,
            PerformerId = 1,
            State = TaskState.ToDo
        };

        var sut = CreateSut();

        // Act
        var result = await sut.AddTaskAsync(task);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Project", result.Error.Message);
        _tasks.Verify(r => r.AddAsync(It.IsAny<DAL.Entities.Task>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddExecutedTaskAsync_ValidTask_CreatesExecutedTask()
    {
        // Arrange
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var executedTask = new ExecutedTask
        {
            Id = 123,
            TaskId = 1,
            TaskName = "Task1"
        };

        var sut = CreateSut();
        var before = DateTime.UtcNow;

        // Act
        var result = await sut.AddExecutedTaskAsync(executedTask);
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(executedTask, result.Value);
        Assert.Equal(0, result.Value!.Id);
        Assert.True(result.Value!.CreatedAt >= before && result.Value!.CreatedAt <= after);

        _executedTasks.Verify(r => r.AddAsync(executedTask, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateTaskByIdAsync_ValidTask_UpdatesSuccessfully()
    {
        // Arrange
        var existing = new DAL.Entities.Task
        {
            Id = 7,
            Name = "OldName",
            Description = "OldDesc",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.ToDo
        };

        _tasks.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>()))
              .ReturnsAsync(existing);
        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        _projects.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Project, bool>>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var update = new DAL.Entities.Task
        {
            Name = "NewName",
            Description = "NewDesc",
            ProjectId = 2,
            PerformerId = 2,
            State = TaskState.Done
        };

        var sut = CreateSut();

        // Act
        var result = await sut.UpdateTaskByIdAsync(7, update);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(existing, result.Value);
        Assert.Equal("NewName", existing.Name);
        Assert.Equal("NewDesc", existing.Description);
        Assert.Equal(2, existing.ProjectId);
        Assert.Equal(2, existing.PerformerId);
        Assert.Equal(TaskState.Done, existing.State);
        Assert.NotNull(existing.FinishedAt); // Done state should set FinishedAt

        _tasks.Verify(r => r.Update(existing), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateTaskByIdAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        _tasks.Setup(r => r.GetByIdAsync(123, It.IsAny<CancellationToken>()))
              .ReturnsAsync((DAL.Entities.Task?)null);

        var sut = CreateSut();

        // Act
        var result = await sut.UpdateTaskByIdAsync(123, new DAL.Entities.Task());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        _tasks.Verify(r => r.Update(It.IsAny<DAL.Entities.Task>()), Times.Never);
    }

    [Fact]
    public async Task UpdateTaskByIdAsync_MissingPerformer_ReturnsFailure()
    {
        // Arrange
        var existing = new DAL.Entities.Task { Id = 1, Name = "T", ProjectId = 1, PerformerId = 1 };
        _tasks.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
              .ReturnsAsync(existing);
        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        var sut = CreateSut();

        // Act
        var result = await sut.UpdateTaskByIdAsync(1, new DAL.Entities.Task { PerformerId = 999, ProjectId = 1 });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        _tasks.Verify(r => r.Update(It.IsAny<DAL.Entities.Task>()), Times.Never);
    }

    [Fact]
    public async Task UpdateTaskByIdAsync_MissingProject_ReturnsFailure()
    {
        // Arrange
        var existing = new DAL.Entities.Task { Id = 1, Name = "T", ProjectId = 1, PerformerId = 1 };
        _tasks.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
              .ReturnsAsync(existing);
        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        _projects.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Project, bool>>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

        var sut = CreateSut();

        // Act
        var result = await sut.UpdateTaskByIdAsync(1, new DAL.Entities.Task { PerformerId = 1, ProjectId = 999 });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        _tasks.Verify(r => r.Update(It.IsAny<DAL.Entities.Task>()), Times.Never);
    }

    [Fact]
    public async Task DeleteTaskByIdAsync_ExistingTask_DeletesSuccessfully()
    {
        // Arrange
        var existing = new DAL.Entities.Task { Id = 10, Name = "Task10", ProjectId = 1, PerformerId = 1 };
        _tasks.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>()))
              .ReturnsAsync(existing);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = CreateSut();

        // Act
        var result = await sut.DeleteTaskByIdAsync(10);

        // Assert
        Assert.True(result.IsSuccess);
        _tasks.Verify(r => r.Remove(existing), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteTaskByIdAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        _tasks.Setup(r => r.GetByIdAsync(77, It.IsAny<CancellationToken>()))
              .ReturnsAsync((DAL.Entities.Task?)null);

        var sut = CreateSut();

        // Act
        var result = await sut.DeleteTaskByIdAsync(77);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        _tasks.Verify(r => r.Remove(It.IsAny<DAL.Entities.Task>()), Times.Never);
    }

    [Fact]
    public async Task UpdateTaskByIdAsync_StateChangeToDone_SetsFinishedAt()
    {
        // Arrange
        var existing = new DAL.Entities.Task
        {
            Id = 1,
            Name = "Task",
            State = TaskState.InProgress,
            FinishedAt = null
        };

        _tasks.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
              .ReturnsAsync(existing);
        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        _projects.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Project, bool>>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

        var update = new DAL.Entities.Task
        {
            Name = "Task",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.Done
        };

        var sut = CreateSut();
        var before = DateTime.UtcNow;

        // Act
        var result = await sut.UpdateTaskByIdAsync(1, update);
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.FinishedAt);
        Assert.True(result.Value!.FinishedAt >= before && result.Value!.FinishedAt <= after);
    }
}
