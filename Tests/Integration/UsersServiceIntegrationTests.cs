using AutoMapper;
using BLL.Mapping;
using BLL.Models.Users;
using BLL.Services;
using DAL.Entities;
using DAL.Repositories.Implementation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests.Integration;

[Collection("Database collection")]
public class UsersServiceIntegrationTests : IAsyncLifetime
{
    private readonly Mock<ILogger<UsersService>> _logger = new();
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
    private readonly DatabaseFixture fixture;

    public UsersServiceIntegrationTests(DatabaseFixture fixture)
    {
        this.fixture = fixture;
    }
    public System.Threading.Tasks.Task InitializeAsync()
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task DisposeAsync()
    {
        fixture.ResetDatabase();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private UsersService CreateService()
    {
        var userRepo = new EfCoreRepository<User>(fixture.Context);
        var teamRepo = new EfCoreRepository<Team>(fixture.Context);
        var uow = new UnitOfWork(fixture.Context);

        return new UsersService(userRepo, teamRepo, uow, _mapper, _logger.Object);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUsersAsync_ReturnsAllUsers()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetUsersAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Count >= 2, $"Expected at least 2 users, got {result.Value.Count}");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserByIdAsync_ExistingId_ReturnsUser()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetUserByIdAsync(1);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.Id);
        Assert.Equal("John", result.Value.FirstName);
        Assert.Equal("Doe", result.Value.LastName);
        Assert.Equal("john@example.com", result.Value.Email);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserByIdAsync_NonExistingId_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetUserByIdAsync(999);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("User with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateUserByIdAsync_ExistingUser_UpdatesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var updatedUser = new UpdateUserDto
        {
            FirstName = "Updated John",
            LastName = "Updated Doe",
            Email = "updatedjohn@example.com",
            TeamId = 2,
            BirthDay = new DateTime(1990, 1, 1)
        };

        // Act
        var result = await service.UpdateUserByIdAsync(1, updatedUser);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.Id);
        Assert.Equal("Updated John", result.Value.FirstName);
        Assert.Equal("Updated Doe", result.Value.LastName);
        Assert.Equal("updatedjohn@example.com", result.Value.Email);
        Assert.Equal(2, result.Value.TeamId);

        // Verify persistence
        var getResult = await service.GetUserByIdAsync(1);
        Assert.True(getResult.IsSuccess);
        Assert.Equal("Updated John", getResult.Value!.FirstName);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateUserByIdAsync_NonExistingUser_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();
        var updatedUser = new UpdateUserDto
        {
            FirstName = "Test",
            LastName = "User",
            Email = "test@example.com",
            TeamId = 1,
            BirthDay = new DateTime(1990, 1, 1)
        };

        // Act
        var result = await service.UpdateUserByIdAsync(999, updatedUser);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("User with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateUserByIdAsync_InvalidTeam_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();
        var updatedUser = new UpdateUserDto
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            TeamId = 999, // Non-existent team
            BirthDay = new DateTime(1990, 1, 1)
        };

        // Act
        var result = await service.UpdateUserByIdAsync(1, updatedUser);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Team with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateUserByIdAsync_NullTeamId_UpdatesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var updatedUser = new UpdateUserDto
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            TeamId = null, // Null team is valid
            BirthDay = new DateTime(1990, 1, 1)
        };

        // Act
        var result = await service.UpdateUserByIdAsync(2, updatedUser);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Id);
        Assert.Null(result.Value.TeamId);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteUserByIdAsync_ExistingUser_DeletesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        
        // Use user 3 which should have no dependencies
        const int userId = 5;

        // Act
        var result = await service.DeleteUserByIdAsync(userId);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify it's deleted
        var getResult = await service.GetUserByIdAsync(userId);
        Assert.True(getResult.IsFailure);
        Assert.Equal("Error.NotFound", getResult.Error.Code);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteUserByIdAsync_NonExistingUser_ReturnsNotFoundError()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.DeleteUserByIdAsync(999);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("User with ID 999 was not found", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateUserByIdAsync_ChangeTeam_UpdatesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var dto = new UpdateUserDto
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            UserName = "john",
            TeamId = 2,
            BirthDay = DateTime.UtcNow.AddYears(-30)
        };

        // Act
        var result = await service.UpdateUserByIdAsync(1, dto);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TeamId);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateUserByIdAsync_UpdateBirthDay_UpdatesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var newBirthDay = new DateTime(1990, 5, 15);
        var dto = new UpdateUserDto
        {
            FirstName = "Jane",
            LastName = "Smith",
            Email = "jane@example.com",
            UserName = "jane",
            TeamId = 1,
            BirthDay = newBirthDay
        };

        // Act
        var result = await service.UpdateUserByIdAsync(2, dto);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(newBirthDay, result.Value!.BirthDay);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancellationToken_CancelledOperation_HandledGracefully()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await service.GetUsersAsync(cts.Token);

        // Assert
        Assert.NotNull(result);
    }
}
