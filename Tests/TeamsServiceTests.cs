using BLL.Services;
using DAL.Entities;
using DAL.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BLL.Models.Teams;

namespace Tests;

public class TeamsServiceTests
{
    private readonly Mock<IRepository<Team>> _teams = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ILogger<TeamsService>> _logger = new();

    private TeamsService CreateSut() => new(_teams.Object, _uow.Object, _logger.Object);

    [Fact]
    public async System.Threading.Tasks.Task GetTeamsAsync_ReturnsAllTeams()
    {
        // Arrange
        var data = new List<Team>
        {
            new() { Id = 1, Name = "Team1", CreatedAt = DateTime.UtcNow },
            new() { Id = 2, Name = "Team2", CreatedAt = DateTime.UtcNow }
        };
        _teams.Setup(r => r.ListAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(data);
        var sut = CreateSut();

        // Act
        var result = await sut.GetTeamsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTeamByIdAsync_Found_ReturnsTeam()
    {
        // Arrange
        var team = new Team { Id = 1, Name = "Team1", CreatedAt = DateTime.UtcNow };
        _teams.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(team);
        var sut = CreateSut();

        // Act
        var result = await sut.GetTeamByIdAsync(1);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(team, result.Value);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTeamByIdAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        _teams.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((Team?)null);
        var sut = CreateSut();

        // Act
        var result = await sut.GetTeamByIdAsync(99);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddTeamAsync_ValidTeam_CreatesTeam()
    {
        // Arrange
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var dto = new CreateTeamDto("NewTeam");
        var sut = CreateSut();

        // Act
        var result = await sut.AddTeamAsync(dto);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("NewTeam", result.Value!.Name);
        _teams.Verify(r => r.AddAsync(It.IsAny<Team>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateTeamByIdAsync_ExistingTeam_UpdatesSuccessfully()
    {
        // Arrange
        var existing = new Team { Id = 1, Name = "OldName", CreatedAt = DateTime.UtcNow };
        _teams.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var dto = new UpdateTeamDto("NewName");
        var sut = CreateSut();

        // Act
        var result = await sut.UpdateTeamByIdAsync(1, dto);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("NewName", result.Value!.Name);
        _teams.Verify(r => r.Update(existing), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateTeamByIdAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        _teams.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((Team?)null);
        var sut = CreateSut();

        // Act
        var result = await sut.UpdateTeamByIdAsync(99, new UpdateTeamDto("Name"));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteTeamByIdAsync_ExistingTeam_DeletesSuccessfully()
    {
        // Arrange
        var team = new Team { Id = 1, Name = "Team1", CreatedAt = DateTime.UtcNow };
        _teams.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(team);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var sut = CreateSut();

        // Act
        var result = await sut.DeleteTeamByIdAsync(1);

        // Assert
        Assert.True(result.IsSuccess);
        _teams.Verify(r => r.Remove(team), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteTeamByIdAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        _teams.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((Team?)null);
        var sut = CreateSut();

        // Act
        var result = await sut.DeleteTeamByIdAsync(99);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
    }
}
