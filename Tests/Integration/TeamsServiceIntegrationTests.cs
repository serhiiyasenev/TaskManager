using AutoMapper;
using BLL.Mapping;
using BLL.Models.Teams;
using BLL.Services;
using DAL.Entities;
using DAL.Repositories.Implementation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests.Integration;

[Collection("Database collection")]
public class TeamsServiceIntegrationTests
{
    private readonly DatabaseFixture _fixture;
    private readonly Mock<ILogger<TeamsService>> _logger = new();
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

    public TeamsServiceIntegrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private TeamsService CreateService()
    {
        var teamRepo = new EfCoreRepository<Team>(_fixture.Context);
        var uow = new UnitOfWork(_fixture.Context);

        return new TeamsService(teamRepo, uow, _mapper, _logger.Object);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTeamsAsync_ReturnsAllTeams()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetTeamsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Count >= 2, $"Expected at least 2 teams, got {result.Value.Count}");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTeamByIdAsync_ExistingId_ReturnsTeam()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetTeamByIdAsync(1);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.Id);
        Assert.NotNull(result.Value.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTeamByIdAsync_NonExistingId_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetTeamByIdAsync(999);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Team with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddTeamAsync_ValidTeam_CreatesTeam()
    {
        // Arrange
        var service = CreateService();
        var newTeam = new CreateTeamDto("New Team");

        // Act
        var result = await service.AddTeamAsync(newTeam);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Id > 0);
        Assert.Equal("New Team", result.Value.Name);
        Assert.True(result.Value.CreatedAt <= DateTime.UtcNow);

        // Verify it's in the database
        var getResult = await service.GetTeamByIdAsync(result.Value.Id);
        Assert.True(getResult.IsSuccess);
        Assert.Equal("New Team", getResult.Value!.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateTeamByIdAsync_ExistingTeam_UpdatesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var updatedTeam = new UpdateTeamDto("Updated Team Alpha");

        // Act
        var result = await service.UpdateTeamByIdAsync(1, updatedTeam);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.Id);
        Assert.Equal("Updated Team Alpha", result.Value.Name);

        // Verify persistence
        var getResult = await service.GetTeamByIdAsync(1);
        Assert.True(getResult.IsSuccess);
        Assert.Equal("Updated Team Alpha", getResult.Value!.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateTeamByIdAsync_NonExistingTeam_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();
        var updatedTeam = new UpdateTeamDto("Updated Team");

        // Act
        var result = await service.UpdateTeamByIdAsync(999, updatedTeam);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Team with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteTeamByIdAsync_ExistingTeam_DeletesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        
        // Create a team to delete
        var newTeam = new CreateTeamDto("Team to Delete");
        var createResult = await service.AddTeamAsync(newTeam);
        Assert.True(createResult.IsSuccess);
        var teamId = createResult.Value!.Id;

        // Act
        var result = await service.DeleteTeamByIdAsync(teamId);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify it's deleted
        var getResult = await service.GetTeamByIdAsync(teamId);
        Assert.True(getResult.IsFailure);
        Assert.Equal("Error.NotFound", getResult.Error.Code);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteTeamByIdAsync_NonExistingTeam_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.DeleteTeamByIdAsync(999);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Team with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddTeamAsync_MultipleTeams_AllCreatedSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var team1Dto = new CreateTeamDto("Development Team");
        var team2Dto = new CreateTeamDto("QA Team");

        // Act
        var result1 = await service.AddTeamAsync(team1Dto);
        var result2 = await service.AddTeamAsync(team2Dto);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.NotEqual(result1.Value!.Id, result2.Value!.Id);
        Assert.Equal("Development Team", result1.Value!.Name);
        Assert.Equal("QA Team", result2.Value!.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateTeamByIdAsync_UpdateMultipleTimes_LastUpdatePersists()
    {
        // Arrange
        var service = CreateService();
        var createDto = new CreateTeamDto("Original Team");
        var createResult = await service.AddTeamAsync(createDto);
        var teamId = createResult.Value!.Id;

        // Act
        await service.UpdateTeamByIdAsync(teamId, new UpdateTeamDto("First Update"));
        await service.UpdateTeamByIdAsync(teamId, new UpdateTeamDto("Second Update"));
        var finalResult = await service.UpdateTeamByIdAsync(teamId, new UpdateTeamDto("Final Update"));

        // Assert
        Assert.True(finalResult.IsSuccess);
        Assert.Equal("Final Update", finalResult.Value!.Name);
        
        // Verify persistence
        var getResult = await service.GetTeamByIdAsync(teamId);
        Assert.Equal("Final Update", getResult.Value!.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancellationToken_CancelledOperation_HandledGracefully()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await service.GetTeamsAsync(cts.Token);

        // Assert
        // The operation should complete (either success or failure) without throwing
        Assert.NotNull(result);
    }
}
