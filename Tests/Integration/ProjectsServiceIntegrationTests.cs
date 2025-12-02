using AutoMapper;
using BLL.Mapping;
using BLL.Services;
using DAL.Entities;
using DAL.Repositories.Implementation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests.Integration;

public class ProjectsServiceIntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private readonly Mock<ILogger<ProjectsService>> _logger = new();
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

    // Don't reset database - use shared fixture data

    private ProjectsService CreateService()
    {
        var projectRepo = new EfCoreRepository<Project>(fixture.Context);
        var userRepo = new EfCoreRepository<User>(fixture.Context);
        var teamRepo = new EfCoreRepository<Team>(fixture.Context);
        var uow = new UnitOfWork(fixture.Context);

        return new ProjectsService(projectRepo, userRepo, teamRepo, uow, _mapper, _logger.Object);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetProjectsAsync_ReturnsAllProjects()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetProjectsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Count >= 2, $"Expected at least 2 projects, got {result.Value.Count}");
        // Note: Other tests may have added projects, so we just check for at least the seed data
    }

    [Fact]
    public async System.Threading.Tasks.Task GetProjectByIdAsync_ExistingId_ReturnsProject()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetProjectByIdAsync(1);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.Id);
        Assert.Equal("Project Alpha", result.Value.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetProjectByIdAsync_NonExistingId_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetProjectByIdAsync(999);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Project with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddProjectAsync_ValidProject_CreatesProject()
    {
        // Arrange
        var service = CreateService();
        var newProject = new Project
        {
            Name = "New Project",
            Description = "New project description",
            AuthorId = 1,
            TeamId = 1,
            Deadline = DateTime.UtcNow.AddDays(90)
        };

        // Act
        var result = await service.AddProjectAsync(newProject);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Id > 0);
        Assert.Equal("New Project", result.Value.Name);
        Assert.True(result.Value.CreatedAt <= DateTime.UtcNow);

        // Verify it's in the database
        var getResult = await service.GetProjectByIdAsync(result.Value.Id);
        Assert.True(getResult.IsSuccess);
        Assert.Equal("New Project", getResult.Value!.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddProjectAsync_InvalidAuthor_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();
        var newProject = new Project
        {
            Name = "New Project",
            Description = "New project description",
            AuthorId = 999, // Non-existent author
            TeamId = 1,
            Deadline = DateTime.UtcNow.AddDays(90)
        };

        // Act
        var result = await service.AddProjectAsync(newProject);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Author with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddProjectAsync_InvalidTeam_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();
        var newProject = new Project
        {
            Name = "New Project",
            Description = "New project description",
            AuthorId = 1,
            TeamId = 999, // Non-existent team
            Deadline = DateTime.UtcNow.AddDays(90)
        };

        // Act
        var result = await service.AddProjectAsync(newProject);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Team with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateProjectByIdAsync_ExistingProject_UpdatesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var updatedProject = new Project
        {
            Name = "Updated Project Alpha",
            Description = "Updated description",
            AuthorId = 2,
            TeamId = 2,
            Deadline = DateTime.UtcNow.AddDays(45)
        };

        // Act
        var result = await service.UpdateProjectByIdAsync(1, updatedProject);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.Id);
        Assert.Equal("Updated Project Alpha", result.Value.Name);
        Assert.Equal("Updated description", result.Value.Description);
        Assert.Equal(2, result.Value.AuthorId);
        Assert.Equal(2, result.Value.TeamId);

        // Verify persistence
        var getResult = await service.GetProjectByIdAsync(1);
        Assert.True(getResult.IsSuccess);
        Assert.Equal("Updated Project Alpha", getResult.Value!.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateProjectByIdAsync_NonExistingProject_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();
        var updatedProject = new Project
        {
            Name = "Updated Project",
            Description = "Updated description",
            AuthorId = 1,
            TeamId = 1,
            Deadline = DateTime.UtcNow.AddDays(45)
        };

        // Act
        var result = await service.UpdateProjectByIdAsync(999, updatedProject);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Project with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateProjectByIdAsync_InvalidAuthor_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();
        var updatedProject = new Project
        {
            Name = "Updated Project",
            Description = "Updated description",
            AuthorId = 999, // Non-existent author
            TeamId = 1,
            Deadline = DateTime.UtcNow.AddDays(45)
        };

        // Act
        var result = await service.UpdateProjectByIdAsync(1, updatedProject);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Author with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateProjectByIdAsync_InvalidTeam_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();
        var updatedProject = new Project
        {
            Name = "Updated Project",
            Description = "Updated description",
            AuthorId = 1,
            TeamId = 999, // Non-existent team
            Deadline = DateTime.UtcNow.AddDays(45)
        };

        // Act
        var result = await service.UpdateProjectByIdAsync(1, updatedProject);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Team with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteProjectByIdAsync_ExistingProject_DeletesSuccessfully()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.DeleteProjectByIdAsync(2);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify it's deleted
        var getResult = await service.GetProjectByIdAsync(2);
        Assert.True(getResult.IsFailure);
        Assert.Equal("Error.NotFound", getResult.Error.Code);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteProjectByIdAsync_NonExistingProject_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.DeleteProjectByIdAsync(999);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Project with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancellationToken_CancelledOperation_HandledGracefully()
    {
        // Arrange
        var service = CreateService();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        // Note: The service catches exceptions and returns Result.Failure
        // Cancellation is handled at the repository level, but the service wraps it
        var result = await service.GetProjectsAsync(cts.Token);

        // Assert
        // The operation should complete (either success or failure) without throwing
        Assert.NotNull(result);
    }
}
