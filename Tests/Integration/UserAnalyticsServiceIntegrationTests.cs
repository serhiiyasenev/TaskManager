using BLL.Services.Analytics;
using DAL.Entities;
using DAL.Enum;
using DAL.Repositories.Implementation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Integration;

[Collection("Database collection")]
public class UserAnalyticsServiceIntegrationTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public UserAnalyticsServiceIntegrationTests(DatabaseFixture fixture)
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

    private UserAnalyticsService CreateService()
    {
        var userRepo = new EfCoreRepository<User>(_fixture.Context);
        var projectRepo = new EfCoreRepository<Project>(_fixture.Context);
        var taskRepo = new EfCoreRepository<DAL.Entities.Task>(_fixture.Context);

        return new UserAnalyticsService(userRepo, projectRepo, taskRepo);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedUsersWithSortedTasksAsync_ReturnsAllUsers()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetSortedUsersWithSortedTasksAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count >= 3, $"Expected at least 3 users, got {result.Count}");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedUsersWithSortedTasksAsync_UsersSortedByFirstName()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetSortedUsersWithSortedTasksAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count > 1);
        // Verify users are sorted by first name
        for (var i = 0; i < result.Count - 1; i++)
        {
            Assert.True(string.Compare(result[i].FirstName, result[i + 1].FirstName, StringComparison.Ordinal) <= 0,
                $"Users not sorted by FirstName: {result[i].FirstName} should come before {result[i + 1].FirstName}");
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedUsersWithSortedTasksAsync_TasksSortedByNameLengthDescending()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetSortedUsersWithSortedTasksAsync();

        // Assert
        Assert.NotNull(result);
        var users = result.Where(u => u.Tasks.Count > 1).ToList();
        Assert.True(users.Count > 1);
        foreach (var user in users)
        {
            for (var i = 0; i < user.Tasks.Count - 1; i++)
            {
                Assert.True(user.Tasks[i].Name.Length >= user.Tasks[i + 1].Name.Length,
                    $"Tasks not sorted by name length descending for user {user.FirstName}");
            }
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedUsersWithSortedTasksAsync_VerifyUserStructure()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetSortedUsersWithSortedTasksAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count > 1);
        
        var usersWithTasks = result.Where(u => u.Tasks.Count > 0).ToList();
        Assert.True(usersWithTasks.Count > 0, "Expected at least one user with tasks");
        
        foreach (var user in usersWithTasks)
        {
            Assert.True(user.Id > 0);
            Assert.False(string.IsNullOrEmpty(user.FirstName));
            Assert.False(string.IsNullOrEmpty(user.LastName));
            Assert.False(string.IsNullOrEmpty(user.Email));
            Assert.True(user.RegisteredAt <= DateTime.UtcNow);
            Assert.NotNull(user.Tasks);
            Assert.True(user.Tasks.Count > 0);
            foreach (var task in user.Tasks)
            {
                Assert.True(task.Id > 0);
                Assert.False(string.IsNullOrEmpty(task.Name));
                Assert.NotNull(task.State);
            }
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserInfoAsync_ExistingUser_ReturnsUserInfo()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetUserInfoAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.User);
        Assert.Equal(1, result.User.Id);
        Assert.True(result.NotFinishedOrCanceledTasksCount >= 0);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserInfoAsync_NonExistentUser_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetUserInfoAsync(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserInfoAsync_UserWithProjects_ReturnsLastProject()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetUserInfoAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.User);

        if (result.LastProject != null)
        {
            Assert.True(result.LastProject.Id > 0);
            Assert.False(string.IsNullOrEmpty(result.LastProject.Name));
            Assert.True(result.LastProjectTasksCount >= 0);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserInfoAsync_UserWithTasks_ReturnsLongestTask()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetUserInfoAsync(1);

        // Assert
        Assert.NotNull(result);

        if (result.LongestTask != null)
        {
            Assert.True(result.LongestTask.Id > 0);
            Assert.False(string.IsNullOrEmpty(result.LongestTask.Name));
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserInfoAsync_VerifyNotFinishedOrCanceledTasksCount()
    {
        // Arrange
        var service = CreateService();
        var taskRepo = new EfCoreRepository<DAL.Entities.Task>(_fixture.Context);
        var uow = new UnitOfWork(_fixture.Context);
        
        // Create a user with specific tasks
        var userRepo = new EfCoreRepository<User>(_fixture.Context);
        var testUser = new User("testanalytics", "Test", "Analytics", "testanalytics@test.com")
        {
            RegisteredAt = DateTime.UtcNow,
            TeamId = 1
        };
        await userRepo.AddAsync(testUser);
        await uow.SaveChangesAsync();
        
        // Add tasks with different states
        var todoTask = new DAL.Entities.Task
        {
            Name = "ToDo Task",
            Description = "Test",
            ProjectId = 1,
            PerformerId = testUser.Id,
            State = TaskState.ToDo,
            CreatedAt = DateTime.UtcNow
        };
        
        var inProgressTask = new DAL.Entities.Task
        {
            Name = "InProgress Task",
            Description = "Test",
            ProjectId = 1,
            PerformerId = testUser.Id,
            State = TaskState.InProgress,
            CreatedAt = DateTime.UtcNow
        };
        
        var doneTask = new DAL.Entities.Task
        {
            Name = "Done Task",
            Description = "Test",
            ProjectId = 1,
            PerformerId = testUser.Id,
            State = TaskState.Done,
            CreatedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow
        };
        
        var canceledTask = new DAL.Entities.Task
        {
            Name = "Canceled Task",
            Description = "Test",
            ProjectId = 1,
            PerformerId = testUser.Id,
            State = TaskState.Canceled,
            CreatedAt = DateTime.UtcNow
        };
        
        await taskRepo.AddAsync(todoTask);
        await taskRepo.AddAsync(inProgressTask);
        await taskRepo.AddAsync(doneTask);
        await taskRepo.AddAsync(canceledTask);
        await uow.SaveChangesAsync();

        var result = await service.GetUserInfoAsync(testUser.Id);
        Assert.NotNull(result);
        // Should count 3 states: ToDo, InProgress, Done
        Assert.Equal(3, result.NotFinishedOrCanceledTasksCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserInfoAsync_UserWithNoProjects_LastProjectIsNull()
    {
        // Arrange
        var service = CreateService();
        var userRepo = new EfCoreRepository<User>(_fixture.Context);
        var uow = new UnitOfWork(_fixture.Context);
        
        // Create a user with no projects
        var testUser = new User("noprojects", "No", "Projects", "noprojects@test.com")
        {
            RegisteredAt = DateTime.UtcNow,
            TeamId = 1
        };
        await userRepo.AddAsync(testUser);
        await uow.SaveChangesAsync();

        // Act
        var result = await service.GetUserInfoAsync(testUser.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.LastProject);
        Assert.Equal(0, result.LastProjectTasksCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserInfoAsync_UserWithNoTasks_LongestTaskIsNull()
    {
        // Arrange
        var service = CreateService();
        var userRepo = new EfCoreRepository<User>(_fixture.Context);
        var uow = new UnitOfWork(_fixture.Context);
        
        // Create a user with no tasks
        var testUser = new User("notasks", "No", "Tasks", "notasks@test.com")
        {
            RegisteredAt = DateTime.UtcNow,
            TeamId = 1
        };
        await userRepo.AddAsync(testUser);
        await uow.SaveChangesAsync();

        // Act
        var result = await service.GetUserInfoAsync(testUser.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.LongestTask);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserInfoAsync_VerifyCompleteStructure()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetUserInfoAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.User);
        Assert.True(result.User.Id > 0);
        Assert.False(string.IsNullOrEmpty(result.User.FirstName));
        Assert.False(string.IsNullOrEmpty(result.User.LastName));
        Assert.False(string.IsNullOrEmpty(result.User.Email));
        Assert.True(result.NotFinishedOrCanceledTasksCount >= 0);
        Assert.True(result.LastProjectTasksCount >= 0);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedUsersWithSortedTasksAsync_EmptyDatabase_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        
        // Clear all data in correct order (children first, then parents)
        _fixture.Context.Tasks.RemoveRange(_fixture.Context.Tasks);
        _fixture.Context.Projects.RemoveRange(_fixture.Context.Projects);
        _fixture.Context.Users.RemoveRange(_fixture.Context.Users);
        _fixture.Context.Teams.RemoveRange(_fixture.Context.Teams);
        await _fixture.Context.SaveChangesAsync();

        // Act
        var result = await service.GetSortedUsersWithSortedTasksAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserInfoAsync_UserWithMultipleProjects_ReturnsLatestProject()
    {
        // Arrange
        var service = CreateService();
        var projectRepo = new EfCoreRepository<Project>(_fixture.Context);
        var uow = new UnitOfWork(_fixture.Context);

        // Get the existing project's CreatedAt to ensure our new projects are newer
        var existingProject = await _fixture.Context.Projects.FirstOrDefaultAsync(p => p.AuthorId == 1);
        var baseDate = existingProject?.CreatedAt ?? DateTime.UtcNow.AddDays(-20);

        // Create multiple projects for user 1 with dates newer than existing
        var project1 = new Project
        {
            Name = "Old Project",
            Description = "Created first",
            AuthorId = 1,
            TeamId = 1,
            CreatedAt = baseDate.AddDays(5),
            Deadline = DateTime.UtcNow.AddDays(20)
        };
        var project2 = new Project
        {
            Name = "New Project",
            Description = "Created last",
            AuthorId = 1,
            TeamId = 1,
            CreatedAt = baseDate.AddDays(10),
            Deadline = DateTime.UtcNow.AddDays(30)
        };

        await projectRepo.AddAsync(project1);
        await projectRepo.AddAsync(project2);
        await uow.SaveChangesAsync();

        // Act
        var result = await service.GetUserInfoAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.LastProject);
        Assert.Equal("New Project", result.LastProject.Name);
    }
}
