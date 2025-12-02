using AutoMapper;
using BLL.Mapping;
using BLL.Services;
using DAL.Entities;
using DAL.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq.Expressions;
using MockQueryable;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Unit;

public class ProjectsServiceTests
{
    private readonly Mock<IRepository<Project>> _projects = new();
    private readonly Mock<IReadRepository<User>> _users = new();
    private readonly Mock<IReadRepository<Team>> _teams = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ILogger<ProjectsService>> _logger = new();
    private readonly IMapper _mapper;

    public ProjectsServiceTests()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = config.CreateMapper();
    }

    private ProjectsService CreateSut() => new(_projects.Object, _users.Object, _teams.Object, _uow.Object, _mapper, _logger.Object);

    [Fact]
    public async Task GetProjectsAsync_ReturnsAll()
    {
        // Arrange
        var data = new List<Project>
        {
            new() { Id = 1, Name = "P1", Description = "D1", AuthorId = 1, TeamId = 1, Deadline = DateTime.UtcNow, 
                    Author = new User(), Team = new Team(), Tasks = new List<DAL.Entities.Task>() },
            new() { Id = 2, Name = "P2", Description = "D2", AuthorId = 2, TeamId = 2, Deadline = DateTime.UtcNow,
                    Author = new User(), Team = new Team(), Tasks = new List<DAL.Entities.Task>() },
        };

        _projects
            .Setup(r => r.Query())
            .Returns(data.BuildMock());

        var sut = CreateSut();

        // Act
        var result = await sut.GetProjectsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Collection(result.Value!, p => Assert.Equal(1, p.Id), p => Assert.Equal(2, p.Id));
    }

    [Fact]
    public async Task GetProjectByIdAsync_Found_ReturnsProject_AndLogsInfo()
    {
        // Arrange
        var project = new Project { Id = 5, Name = "P5", Description = "D5", AuthorId = 1, TeamId = 1, Deadline = DateTime.UtcNow,
                                   Author = new User(), Team = new Team(), Tasks = new List<DAL.Entities.Task>() };
        
        _projects.Setup(r => r.Query())
                 .Returns(new List<Project> { project }.BuildMock());

        var sut = CreateSut();

        // Act
        var result = await sut.GetProjectByIdAsync(5);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Id);
        Assert.Equal("P5", result.Value!.Name);

        _logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retrieving project 5")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProjectByIdAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        _projects.Setup(r => r.Query())
                 .Returns(new List<Project>().BuildMock());

        var sut = CreateSut();

        // Act
        var result = await sut.GetProjectByIdAsync(99);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task AddProjectAsync_HappyPath_AddsAndSaves_SetsIdAndCreatedAt()
    {
        // Arrange
        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _teams.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Team, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var project = new Project
        {
            Id = 123,
            Name = "NewP",
            Description = "Desc",
            AuthorId = 10,
            TeamId = 20,
            Deadline = DateTime.UtcNow.AddDays(7)
        };

        var sut = CreateSut();
        var before = DateTime.UtcNow;

        // Act
        var result = await sut.AddProjectAsync(project);
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(project, result.Value);
        Assert.Equal(0, result.Value!.Id);
        Assert.True(result.Value!.CreatedAt >= before && result.Value!.CreatedAt <= after);

        _projects.Verify(r => r.AddAsync(project, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddProjectAsync_MissingAuthor_ReturnsFailure_NoAddNoSave()
    {
        // Arrange
        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var sut = CreateSut();
        var project = new Project { Name = "P", Description = "D", AuthorId = 999, TeamId = 1, Deadline = DateTime.UtcNow };

        // Act
        var result = await sut.AddProjectAsync(project);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        _projects.Verify(r => r.AddAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddProjectAsync_MissingTeam_ReturnsFailure_NoAddNoSave()
    {
        // Arrange
        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _teams.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Team, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var sut = CreateSut();
        var project = new Project { Name = "P", Description = "D", AuthorId = 1, TeamId = 999, Deadline = DateTime.UtcNow };

        // Act
        var result = await sut.AddProjectAsync(project);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        _projects.Verify(r => r.AddAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateProjectByIdAsync_HappyPath_UpdatesAndSaves()
    {
        // Arrange
        var existing = new Project
        {
            Id = 7,
            Name = "Old",
            Description = "OldD",
            AuthorId = 1,
            TeamId = 1,
            Deadline = DateTime.UtcNow.AddDays(1)
        };

        _projects.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _teams.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Team, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var update = new Project
        {
            Id = 7,
            Name = "New",
            Description = "NewD",
            AuthorId = 10,
            TeamId = 20,
            Deadline = DateTime.UtcNow.AddDays(5)
        };

        var sut = CreateSut();

        // Act
        var result = await sut.UpdateProjectByIdAsync(7, update);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(existing, result.Value);
        Assert.Equal("New", existing.Name);
        Assert.Equal("NewD", existing.Description);
        Assert.Equal(10, existing.AuthorId);
        Assert.Equal(20, existing.TeamId);
        Assert.Equal(update.Deadline, existing.Deadline);

        _projects.Verify(r => r.Update(existing), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateProjectByIdAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        _projects.Setup(r => r.GetByIdAsync(123, It.IsAny<CancellationToken>())).ReturnsAsync((Project?)null);

        var sut = CreateSut();

        // Act
        var result = await sut.UpdateProjectByIdAsync(123, new Project());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        _projects.Verify(r => r.Update(It.IsAny<Project>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateProjectByIdAsync_MissingAuthor_ReturnsFailure_NotUpdated()
    {
        // Arrange
        var existing = new Project { Id = 1, Name = "P", Description = "D", AuthorId = 1, TeamId = 1, Deadline = DateTime.UtcNow };
        _projects.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _teams.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Team, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var sut = CreateSut();

        // Act
        var result = await sut.UpdateProjectByIdAsync(1, new Project { AuthorId = 999, TeamId = 1, Name = "N", Description = "D", Deadline = DateTime.UtcNow });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        _projects.Verify(r => r.Update(It.IsAny<Project>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateProjectByIdAsync_MissingTeam_ReturnsFailure_NotUpdated()
    {
        // Arrange
        var existing = new Project { Id = 1, Name = "P", Description = "D", AuthorId = 1, TeamId = 1, Deadline = DateTime.UtcNow };
        _projects.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        _users.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _teams.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Team, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var sut = CreateSut();

        // Act
        var result = await sut.UpdateProjectByIdAsync(1, new Project { AuthorId = 1, TeamId = 999, Name = "N", Description = "D", Deadline = DateTime.UtcNow });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        _projects.Verify(r => r.Update(It.IsAny<Project>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteProjectByIdAsync_HappyPath_RemovesAndSaves()
    {
        // Arrange
        var existing = new Project { Id = 10, Name = "X", Description = "Y", AuthorId = 1, TeamId = 1, Deadline = DateTime.UtcNow };
        _projects.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var sut = CreateSut();

        // Act
        await sut.DeleteProjectByIdAsync(10);

        // Assert
        _projects.Verify(r => r.Remove(existing), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteProjectByIdAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        _projects.Setup(r => r.GetByIdAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync((Project?)null);

        var sut = CreateSut();

        // Act
        var result = await sut.DeleteProjectByIdAsync(77);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        _projects.Verify(r => r.Remove(It.IsAny<Project>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
