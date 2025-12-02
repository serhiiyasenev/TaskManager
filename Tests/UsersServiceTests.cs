using BLL.Services;
using BLL.Models.Users;
using DAL.Entities;
using DAL.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Linq.Expressions;

namespace Tests;

public class UsersServiceTests
{
    private readonly Mock<IRepository<User>> _users = new();
    private readonly Mock<IReadRepository<Team>> _teams = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ILogger<UsersService>> _logger = new();

    private UsersService CreateSut() => new(_users.Object, _teams.Object, _uow.Object, _logger.Object);

    [Fact]
    public async System.Threading.Tasks.Task GetUsersAsync_ReturnsAllUsers()
    {
        // Arrange
        var data = new List<User>
        {
            new() { Id = 1, FirstName = "John", LastName = "Doe", Email = "john@test.com", RegisteredAt = DateTime.UtcNow },
            new() { Id = 2, FirstName = "Jane", LastName = "Smith", Email = "jane@test.com", RegisteredAt = DateTime.UtcNow }
        };
        _users.Setup(r => r.ListAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(data);
        var sut = CreateSut();

        // Act
        var result = await sut.GetUsersAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserByIdAsync_Found_ReturnsUser()
    {
        // Arrange
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe", Email = "john@test.com", RegisteredAt = DateTime.UtcNow };
        _users.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var sut = CreateSut();

        // Act
        var result = await sut.GetUserByIdAsync(1);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("John", result.Value!.FirstName);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetUserByIdAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        _users.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        var sut = CreateSut();

        // Act
        var result = await sut.GetUserByIdAsync(99);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateUserByIdAsync_ValidUser_UpdatesSuccessfully()
    {
        // Arrange
        var existing = new User { Id = 1, FirstName = "Old", LastName = "Name", Email = "old@test.com", RegisteredAt = DateTime.UtcNow };
        _users.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _teams.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Team, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var dto = new UpdateUserDto { TeamId = 1, FirstName = "New", LastName = "Name", Email = "new@test.com", UserName = "newuser", BirthDay = DateTime.UtcNow };
        var sut = CreateSut();

        // Act
        var result = await sut.UpdateUserByIdAsync(1, dto);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("New", result.Value!.FirstName);
        _users.Verify(r => r.Update(existing), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateUserByIdAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        _users.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        var sut = CreateSut();

        // Act
        var result = await sut.UpdateUserByIdAsync(99, new UpdateUserDto { FirstName = "Name", LastName = "Last", Email = "email@test.com", UserName = "user", BirthDay = DateTime.UtcNow });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateUserByIdAsync_InvalidTeam_ReturnsFailure()
    {
        // Arrange
        var existing = new User { Id = 1, FirstName = "John", LastName = "Doe", Email = "john@test.com", RegisteredAt = DateTime.UtcNow };
        _users.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _teams.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Team, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var dto = new UpdateUserDto { TeamId = 999, FirstName = "John", LastName = "Doe", Email = "john@test.com", UserName = "john", BirthDay = DateTime.UtcNow };
        var sut = CreateSut();

        // Act
        var result = await sut.UpdateUserByIdAsync(1, dto);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
        Assert.Contains("Team", result.Error.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteUserByIdAsync_ExistingUser_DeletesSuccessfully()
    {
        // Arrange
        var user = new User { Id = 1, FirstName = "John", LastName = "Doe", Email = "john@test.com", RegisteredAt = DateTime.UtcNow };
        _users.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var sut = CreateSut();

        // Act
        var result = await sut.DeleteUserByIdAsync(1);

        // Assert
        Assert.True(result.IsSuccess);
        _users.Verify(r => r.Remove(user), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteUserByIdAsync_NotFound_ReturnsFailure()
    {
        // Arrange
        _users.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        var sut = CreateSut();

        // Act
        var result = await sut.DeleteUserByIdAsync(99);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Error.NotFound", result.Error.Code);
    }
}
