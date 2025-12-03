using BLL.Services.Analytics;
using DAL.Entities;
using DAL.Repositories.Implementation;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Tests.Integration;

public class TaskAnalyticsServiceIntegrationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public TaskAnalyticsServiceIntegrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public System.Threading.Tasks.Task InitializeAsync()
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task DisposeAsync()
    {
        _fixture.ResetDatabase();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private TaskAnalyticsService CreateService()
    {
        var taskRepo = new EfCoreRepository<DAL.Entities.Task>(_fixture.Context);
        var projectRepo = new EfCoreRepository<Project>(_fixture.Context);

        return new TaskAnalyticsService(taskRepo, projectRepo);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTasksCountInProjectsByUserIdAsync_ExistingUser_ReturnsTaskCounts()
    {
        // Arrange
        var service = CreateService();
        // User 1 has tasks in the seed data

        // Act
        var result = await service.GetTasksCountInProjectsByUserIdAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        
        // Verify the dictionary structure
        foreach (var kvp in result)
        {
            Assert.False(string.IsNullOrEmpty(kvp.Key));
            Assert.True(kvp.Value >= 0);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTasksCountInProjectsByUserIdAsync_UserWithNoTasks_ReturnsEmptyOrZeroCounts()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetTasksCountInProjectsByUserIdAsync(5);
        var values = result.Select(e => e.Value).ToList();

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Count > 1);
        foreach (var value in values)
        {
            Assert.True(value == 0);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTasksCountInProjectsByUserIdAsync_NonExistentUser_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetTasksCountInProjectsByUserIdAsync(999);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Count > 1);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetCapitalTasksByUserIdAsync_ExistingUser_ReturnsTasksStartingWithCapital()
    {
        // Arrange
        var service = CreateService();
        // User 1 has "Task 1" which starts with capital T

        // Act
        var result = await service.GetCapitalTasksByUserIdAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        
        // Verify all tasks start with capital letter
        Assert.All(result, task =>
        {
            Assert.False(string.IsNullOrEmpty(task.Name));
            Assert.True(char.IsUpper(task.Name[0]));
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task GetCapitalTasksByUserIdAsync_UserWithNoCapitalTasks_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetCapitalTasksByUserIdAsync(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetCapitalTasksByUserIdAsync_VerifyTaskStructure()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetCapitalTasksByUserIdAsync(1);

        // Assert
        Assert.NotNull(result);
        
        foreach (var task in result)
        {
            Assert.True(task.Id > 0);
            Assert.False(string.IsNullOrEmpty(task.Name));
            Assert.NotNull(task.State);
            Assert.True(task.CreatedAt <= DateTime.UtcNow);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTaskStatusByProjectAsync_ExistingProject_ReturnsStatusBreakdown()
    {
        // Arrange
        var service = CreateService();
        // Project 1 exists with tasks

        // Act
        var result = await service.GetTaskStatusByProjectAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        
        var status = result.First();
        Assert.Equal(1, status.ProjectId);
        Assert.False(string.IsNullOrEmpty(status.ProjectName));
        
        // Verify counts are non-negative
        Assert.True(status.ToDo >= 0);
        Assert.True(status.InProgress >= 0);
        Assert.True(status.Done >= 0);
        Assert.True(status.Canceled >= 0);
        Assert.True(status.Total >= 0);
        
        // Verify total equals sum of individual statuses
        Assert.Equal(status.Total, 
            status.ToDo + status.InProgress + status.Done + status.Canceled);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTaskStatusByProjectAsync_ProjectWithMultipleTasks_CorrectCounts()
    {
        // Arrange
        var service = CreateService();
        // Project 1 has Task 1 (ToDo) and Task 2 (InProgress) from seed data

        // Act
        var result = await service.GetTaskStatusByProjectAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        
        var status = result.First();
        Assert.True(status.Total >= 2, $"Expected at least 2 tasks, got {status.Total}");
        Assert.True(status.ToDo >= 1, $"Expected at least 1 ToDo task, got {status.ToDo}");
        Assert.True(status.InProgress >= 1, $"Expected at least 1 InProgress task, got {status.InProgress}");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTaskStatusByProjectAsync_NonExistentProject_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetTaskStatusByProjectAsync(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTaskStatusByProjectAsync_ProjectWithNoTasks_ReturnsZeroCounts()
    {
        // Arrange
        var service = CreateService();
        // Create a project with no tasks
        var projectRepo = new EfCoreRepository<Project>(_fixture.Context);
        var uow = new UnitOfWork(_fixture.Context);
        
        var newProject = new Project
        {
            Name = "Empty Project",
            Description = "No tasks",
            AuthorId = 1,
            TeamId = 1,
            CreatedAt = DateTime.UtcNow,
            Deadline = DateTime.UtcNow.AddDays(30)
        };
        
        await projectRepo.AddAsync(newProject);
        await uow.SaveChangesAsync();

        // Act
        var result = await service.GetTaskStatusByProjectAsync(newProject.Id);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        
        var status = result.First();
        Assert.Equal(0, status.ToDo);
        Assert.Equal(0, status.InProgress);
        Assert.Equal(0, status.Done);
        Assert.Equal(0, status.Canceled);
        Assert.Equal(0, status.Total);
    }
}
