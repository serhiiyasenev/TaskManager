using DAL.Context;
using DAL.Entities;
using DAL.Enum;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Integration;

[CollectionDefinition("Database collection")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }

public class DatabaseFixture : IDisposable
{
    public TaskContext Context { get; private set; }

    public DatabaseFixture()
    {
        var options = new DbContextOptionsBuilder<TaskContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        Context = new TaskContext(options);

        // Seed test data
        SeedTestData().Wait();
    }

    private async System.Threading.Tasks.Task SeedTestData()
    {
        // Create test teams
        var team1 = new Team { Id = 1, Name = "Team Alpha", CreatedAt = DateTime.UtcNow };
        var team2 = new Team { Id = 2, Name = "Team Beta", CreatedAt = DateTime.UtcNow };
        Context.Teams.AddRange(team1, team2);

        // Create test users
        var user1 = new User("user1", "John", "Doe", "john@example.com")
        {
            Id = 1,
            Email = "john@example.com",
            RegisteredAt = DateTime.UtcNow,
            TeamId = 1
        };

        var user2 = new User("user2", "Jane", "Smith", "jane@example.com")
        {
            Id = 2,
            Email = "jane@example.com",
            RegisteredAt = DateTime.UtcNow,
            TeamId = 1
        };

        var user3 = new User("user3", "Bob", "Johnson", "bob@example.com")
        {
            Id = 3,
            Email = "bob@example.com",
            RegisteredAt = DateTime.UtcNow,
            TeamId = 2
        };

        var user4 = new User("user4", "Serhii", "Test", "serhii@test.com")
        {
            Id = 4,
            Email = "serhii@test.com",
            RegisteredAt = DateTime.UtcNow,
            TeamId = 2
        };

        var user5 = new User("user5", "5", "Test", "5@test.com")
        {
            Id = 5,
            Email = "5@test.com",
            RegisteredAt = DateTime.UtcNow,
            TeamId = 2
        };

        Context.Users.AddRange(user1, user2, user3, user4, user5);

        // Create test projects
        var project1 = new Project
        {
            Id = 1,
            Name = "Project Alpha",
            Description = "First test project",
            AuthorId = 1,
            TeamId = 1,
            CreatedAt = DateTime.UtcNow,
            Deadline = DateTime.UtcNow.AddDays(30)
        };

        var project2 = new Project
        {
            Id = 2,
            Name = "Project Beta",
            Description = "Second test project",
            AuthorId = 2,
            TeamId = 1,
            CreatedAt = DateTime.UtcNow,
            Deadline = DateTime.UtcNow.AddDays(60)
        };

        var project3 = new Project
        {
            Id = 3,
            Name = "Project 3",
            Description = "3 test project",
            AuthorId = 4,
            TeamId = 2,
            CreatedAt = DateTime.UtcNow,
            Deadline = DateTime.UtcNow.AddDays(60)
        };

        Context.Projects.AddRange(project1, project2, project3);

        // Create test tasks
        var task1 = new DAL.Entities.Task
        {
            Id = 1,
            Name = "Task 1",
            Description = "First test task",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.ToDo,
            CreatedAt = DateTime.UtcNow
        };

        var task2 = new DAL.Entities.Task
        {
            Id = 2,
            Name = "Task 2",
            Description = "Second test task",
            ProjectId = 1,
            PerformerId = 2,
            State = TaskState.InProgress,
            CreatedAt = DateTime.UtcNow
        };

        var task3 = new DAL.Entities.Task
        {
            Id = 3,
            Name = "Task 3",
            Description = "3 test task",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.Done,
            CreatedAt = DateTime.UtcNow
        };

        var task4 = new DAL.Entities.Task
        {
            Id = 4,
            Name = "Task 4",
            Description = "4 test task",
            ProjectId = 1,
            PerformerId = 2,
            State = TaskState.Canceled,
            CreatedAt = DateTime.UtcNow
        };

        var task5 = new DAL.Entities.Task
        {
            Id = 5,
            Name = "Task 5",
            Description = "5 test task",
            ProjectId = 2,
            PerformerId = 3,
            State = TaskState.ToDo,
            CreatedAt = DateTime.UtcNow
        };

        var task6 = new DAL.Entities.Task
        {
            Id = 6,
            Name = "Task 6",
            Description = "6 test task",
            ProjectId = 2,
            PerformerId = 4,
            State = TaskState.InProgress,
            CreatedAt = DateTime.UtcNow
        };

        Context.Tasks.AddRange(task1, task2, task3, task4, task5, task6);

        await Context.SaveChangesAsync();
    }

    public void ResetDatabase()
    {
        // Clear change tracker
        Context.ChangeTracker.Clear();
        
        // Remove all data
        Context.Tasks.RemoveRange(Context.Tasks);
        Context.Projects.RemoveRange(Context.Projects);
        Context.Users.RemoveRange(Context.Users);
        Context.Teams.RemoveRange(Context.Teams);
        Context.SaveChanges();
        
        // Re-seed
        SeedTestData().Wait();
    }

    public void Dispose()
    {
        Context?.Dispose();
    }
}
