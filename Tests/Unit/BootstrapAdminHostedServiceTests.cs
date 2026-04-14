using BLL.Configuration;
using DAL.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WebAPI;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Unit;

public class BootstrapAdminHostedServiceTests
{
    [Fact]
    public async Task StartAsync_Disabled_DoesNothing()
    {
        // Arrange
        var options = new BootstrapAdminOptions { Enabled = false };
        var roleManager = CreateRoleManager();
        var userManager = CreateUserManager();
        var sut = CreateSut(options, roleManager, userManager);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        roleManager.Verify(x => x.RoleExistsAsync(It.IsAny<string>()), Times.Never);
        roleManager.Verify(x => x.CreateAsync(It.IsAny<IdentityRole<int>>()), Times.Never);
        userManager.Verify(x => x.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        userManager.Verify(x => x.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
        userManager.Verify(x => x.AddToRoleAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("", "Password1!")]
    [InlineData("admin@example.com", "")]
    [InlineData("", "")]
    public async Task StartAsync_EnabledWithoutEmailOrPassword_ThrowsInvalidOperationException(string email, string password)
    {
        // Arrange
        var options = new BootstrapAdminOptions { Enabled = true, Email = email, Password = password };
        var sut = CreateSut(options);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("Email/Password are not configured", ex.Message);
    }

    [Fact]
    public async Task StartAsync_ExistingUserInAdminRole_DoesNotCreateRoleOrUser()
    {
        // Arrange
        var options = new BootstrapAdminOptions
        {
            Enabled = true,
            Email = "admin@example.com",
            Password = "Password1!"
        };

        var existingUser = new User("admin", "System", "Admin", "admin@example.com");
        var roleManager = CreateRoleManager();
        var userManager = CreateUserManager();

        roleManager.Setup(x => x.RoleExistsAsync("admin")).ReturnsAsync(true);
        userManager.Setup(x => x.FindByEmailAsync(options.Email)).ReturnsAsync(existingUser);
        userManager.Setup(x => x.IsInRoleAsync(existingUser, "admin")).ReturnsAsync(true);

        var sut = CreateSut(options, roleManager, userManager);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert
        roleManager.Verify(x => x.CreateAsync(It.IsAny<IdentityRole<int>>()), Times.Never);
        userManager.Verify(x => x.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
        userManager.Verify(x => x.AddToRoleAsync(It.IsAny<User>(), "admin"), Times.Never);
    }

    [Fact]
    public async Task StartAsync_RoleCreationFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new BootstrapAdminOptions
        {
            Enabled = true,
            Email = "admin@example.com",
            Password = "Password1!"
        };

        var roleManager = CreateRoleManager();
        var userManager = CreateUserManager();

        roleManager.Setup(x => x.RoleExistsAsync("admin")).ReturnsAsync(false);
        roleManager.Setup(x => x.CreateAsync(It.IsAny<IdentityRole<int>>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role create error" }));

        var sut = CreateSut(options, roleManager, userManager);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("Failed to create 'admin' role", ex.Message);
        Assert.Contains("Role create error", ex.Message);
    }

    [Fact]
    public async Task StartAsync_UserCreationFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new BootstrapAdminOptions
        {
            Enabled = true,
            Email = "admin@example.com",
            Password = "Password1!",
            FirstName = "System",
            LastName = "Admin"
        };

        var roleManager = CreateRoleManager();
        var userManager = CreateUserManager();

        roleManager.Setup(x => x.RoleExistsAsync("admin")).ReturnsAsync(true);
        userManager.Setup(x => x.FindByEmailAsync(options.Email)).ReturnsAsync((User?)null);
        userManager.Setup(x => x.CreateAsync(It.IsAny<User>(), options.Password))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "User create error" }));

        var sut = CreateSut(options, roleManager, userManager);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("Failed to create bootstrap admin user", ex.Message);
        Assert.Contains("User create error", ex.Message);
    }

    private static BootstrapAdminHostedService CreateSut(
        BootstrapAdminOptions bootstrapOptions,
        Mock<RoleManager<IdentityRole<int>>>? roleManager = null,
        Mock<UserManager<User>>? userManager = null)
    {
        var services = new ServiceCollection();
        if (roleManager is not null)
        {
            services.AddSingleton(roleManager.Object);
        }

        if (userManager is not null)
        {
            services.AddSingleton(userManager.Object);
        }

        var provider = services.BuildServiceProvider();
        var logger = new Mock<ILogger<BootstrapAdminHostedService>>();

        return new BootstrapAdminHostedService(provider, Options.Create(bootstrapOptions), logger.Object);
    }

    private static Mock<UserManager<User>> CreateUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(
            store.Object,
            null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static Mock<RoleManager<IdentityRole<int>>> CreateRoleManager()
    {
        var store = new Mock<IRoleStore<IdentityRole<int>>>();
        return new Mock<RoleManager<IdentityRole<int>>>(
            store.Object,
            null!, null!, null!, null!);
    }
}
