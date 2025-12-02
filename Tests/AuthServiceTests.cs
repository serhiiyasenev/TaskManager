using BLL.Exceptions;
using BLL.Models.Users;
using BLL.Services;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task RegisterAsync_Success_ReturnsSuccessMessage()
    {
        // Arrange
        var userManager = CreateUserManager();
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
                   .ReturnsAsync(IdentityResult.Success);

        var signInManager = CreateSignInManager(userManager.Object);
        var jwtOptions = CreateJwtOptions();
        var sut = CreateSut(userManager, signInManager, jwtOptions);

        var dto = new RegisterUserDto(
            UserName: "john",
            FirstName: "John",
            LastName: "Doe",
            Email: "john@example.com",
            Password: "Password1!");

        // Act
        var result = await sut.RegisterAsync(dto);

        // Assert
        Assert.Equal("User registered successfully.", result);
    }

    [Fact]
    public async Task RegisterAsync_Failed_ReturnsAggregatedErrors()
    {
        // Arrange
        var userManager = CreateUserManager();
        var identityErrors = new[]
        {
            new IdentityError { Description = "E1" },
            new IdentityError { Description = "E2" }
        };
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
                   .ReturnsAsync(IdentityResult.Failed(identityErrors));

        var signInManager = CreateSignInManager(userManager.Object);
        var jwtOptions = CreateJwtOptions();
        var sut = CreateSut(userManager, signInManager, jwtOptions);

        var dto = new RegisterUserDto(
            UserName: "john",
            FirstName: "John",
            LastName: "Doe",
            Email: "john@example.com",
            Password: "Password1!");

        // Act
        var result = await sut.RegisterAsync(dto);

        // Assert
        Assert.Equal("E1 E2", result);
    }

    [Fact]
    public async Task LoginAsync_UserNotFound_ThrowsNotFound()
    {
        // Arrange
        var userManager = CreateUserManager();
        userManager.Setup(m => m.FindByEmailAsync("missing@example.com")).ReturnsAsync((User?)null);

        var signInManager = CreateSignInManager(userManager.Object);
        var jwtOptions = CreateJwtOptions();
        var sut = CreateSut(userManager, signInManager, jwtOptions);
        var dto = new LoginUserDto(Email: "missing@example.com", Password: "x");

        // Act
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => sut.LoginAsync(dto));

        // Assert
        Assert.Contains("No user found. Please register.", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_LockedOut_ThrowsNotFound()
    {
        // Arrange
        var user = new User("u", "f", "l", "u@example.com") { Id = 10, Email = "u@example.com" };

        var userManager = CreateUserManager();
        userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);

        var signInManager = CreateSignInManager(userManager.Object);
        signInManager.Setup(s => s.CheckPasswordSignInAsync(user, "pwd", true))
                     .ReturnsAsync(SignInResult.LockedOut);

        var jwtOptions = CreateJwtOptions();
        var sut = CreateSut(userManager, signInManager, jwtOptions);
        var dto = new LoginUserDto(Email: user.Email, Password: "pwd");

        // Act
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => sut.LoginAsync(dto));

        // Assert
        Assert.Contains("Account is locked. Try again later.", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_NotAllowed_ThrowsNotFound()
    {
        // Arrange
        var user = new User("u", "f", "l", "u@example.com") { Id = 11, Email = "u@example.com" };

        var userManager = CreateUserManager();
        userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);

        var signInManager = CreateSignInManager(userManager.Object);
        signInManager.Setup(s => s.CheckPasswordSignInAsync(user, "pwd", true))
                     .ReturnsAsync(SignInResult.NotAllowed);

        var jwtOptions = CreateJwtOptions();
        var sut = CreateSut(userManager, signInManager, jwtOptions);
        var dto = new LoginUserDto(Email: user.Email, Password: "pwd");

        // Act
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => sut.LoginAsync(dto));

        // Assert
        Assert.Contains("Sign-in is not allowed for this account.", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_BadPassword_ThrowsNotFound_AndLogsError()
    {
        // Arrange
        var user = new User("u", "f", "l", "u@example.com") { Id = 12, Email = "u@example.com" };

        var userManager = CreateUserManager();
        userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);

        var signInManager = CreateSignInManager(userManager.Object);
        signInManager.Setup(s => s.CheckPasswordSignInAsync(user, "wrong", true))
                     .ReturnsAsync(SignInResult.Failed);

        var jwtOptions = CreateJwtOptions();
        var logger = new Mock<ILogger<AuthService>>();
        var sut = new AuthService(userManager.Object, signInManager.Object, jwtOptions, logger.Object);
        var dto = new LoginUserDto(Email: user.Email, Password: "wrong");

        // Act
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => sut.LoginAsync(dto));

        // Assert
        Assert.Contains("Incorrect password, please try again.", ex.Message);

        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Incorrect password")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_Success_ReturnsTokenWithClaims_AndId()
    {
        // Arrange
        var user = new User("user1", "John", "Doe", "john@example.com")
        {
            Id = 42,
            Email = "john@example.com",
            FirstName = "John",
            LastName = "Doe",
            UserName = "user1"
        };

        var roles = new List<string> { "admin", "manager" };

        var userManager = CreateUserManager();
        userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        userManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(roles);

        var signInManager = CreateSignInManager(userManager.Object);
        signInManager.Setup(s => s.CheckPasswordSignInAsync(user, "Password1!", true))
                     .ReturnsAsync(SignInResult.Success);

        var jwtOptions = CreateJwtOptions("SuperStrongKey_12345678901234567890");
        var sut = CreateSut(userManager, signInManager, jwtOptions);
        var dto = new LoginUserDto(Email: user.Email, Password: "Password1!");

        // Act
        var result = await sut.LoginAsync(dto);

        // Assert
        Assert.Equal(user.Id, result.Id);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);

        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString());
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == user.Email);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.PreferredUsername && c.Value == user.UserName);
        Assert.Contains(jwt.Claims, c => c.Type == "firstName" && c.Value == user.FirstName);
        Assert.Contains(jwt.Claims, c => c.Type == "lastName" && c.Value == user.LastName);

        foreach (var r in roles)
        {
            Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Role && c.Value == r);
        }
    }

    private static Mock<UserManager<User>> CreateUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(
            store.Object,
            null, null, null, null, null, null, null, null
        );
    }

    private static Mock<SignInManager<User>> CreateSignInManager(UserManager<User> userManager)
    {
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<User>>();

        return new Mock<SignInManager<User>>(
            userManager,
            contextAccessor.Object,
            claimsFactory.Object,
            null, null, null, null
        );
    }

    private static IOptions<BLL.Configuration.JwtOptions> CreateJwtOptions(string key = "VeryStrongAndLongJwtSigningKey_1234567890")
    {
        var options = new BLL.Configuration.JwtOptions
        {
            Key = key,
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            ExpirationMinutes = 30
        };
        return Options.Create(options);
    }

    private static AuthService CreateSut(
        Mock<UserManager<User>> um,
        Mock<SignInManager<User>> sm,
        IOptions<BLL.Configuration.JwtOptions> jwtOptions,
        Mock<ILogger<AuthService>>? logger = null)
    {
        return new AuthService(um.Object, sm.Object, jwtOptions, (logger ?? new Mock<ILogger<AuthService>>()).Object);
    }
}
