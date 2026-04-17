using System.Diagnostics.CodeAnalysis;
using BLL.Configuration;
using DAL.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Net.Mail;
using SystemTask = System.Threading.Tasks.Task;

namespace WebAPI;

[ExcludeFromCodeCoverage]
public sealed class BootstrapAdminHostedService(
    IServiceProvider serviceProvider,
    IOptions<BootstrapAdminOptions> options,
    ILogger<BootstrapAdminHostedService> logger) : IHostedService
{
    private const string AdminRoleName = "admin";
    private const string PlaceholderPassword = "__SET_IN_USER_SECRETS_OR_ENV__";

    public async SystemTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bootstrap = options.Value;
        if (!bootstrap.Enabled)
        {
            logger.LogInformation("Bootstrap admin initialization is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(bootstrap.Email) || string.IsNullOrWhiteSpace(bootstrap.Password))
        {
            throw new InvalidOperationException("BootstrapAdmin is enabled but Email/Password are not configured.");
        }

        if (string.Equals(bootstrap.Password.Trim(), PlaceholderPassword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("BootstrapAdmin password is a placeholder value. Configure a real password via user-secrets or environment variables.");
        }

        try
        {
            _ = new MailAddress(bootstrap.Email);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("BootstrapAdmin Email is not a valid email address.", ex);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var scope = serviceProvider.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        cancellationToken.ThrowIfCancellationRequested();

        if (!await roleManager.RoleExistsAsync(AdminRoleName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var roleCreateResult = await roleManager.CreateAsync(new IdentityRole<int>(AdminRoleName));
            if (!roleCreateResult.Succeeded)
            {
                throw new InvalidOperationException($"Failed to create '{AdminRoleName}' role: {string.Join("; ", roleCreateResult.Errors.Select(e => e.Description))}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(bootstrap.Email);
        if (user is not null)
        {
            if (await userManager.IsInRoleAsync(user, AdminRoleName))
            {
                logger.LogInformation("Bootstrap admin user already exists and is in '{AdminRoleName}' role. Skipping bootstrap.", AdminRoleName);
                return;
            }

            throw new InvalidOperationException(
                $"BootstrapAdmin is enabled for '{bootstrap.Email}', but a user with that email already exists and is not in '{AdminRoleName}' role. " +
                "Refusing to modify roles for an existing account during bootstrap. " +
                "Remove or rename the existing user, choose a different BootstrapAdmin email, or disable BootstrapAdmin after initial setup.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var userName = string.IsNullOrWhiteSpace(bootstrap.UserName)
            ? bootstrap.Email.Split('@')[0]
            : bootstrap.UserName;

        user = new User
        {
            UserName = userName,
            Email = bootstrap.Email,
            FirstName = bootstrap.FirstName,
            LastName = bootstrap.LastName,
            BirthDay = DateTime.UtcNow.Date,
            EmailConfirmed = bootstrap.EmailConfirmed,
            RegisteredAt = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, bootstrap.Password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException($"Failed to create bootstrap admin user: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
        }

        logger.LogInformation("Bootstrap admin user created.");
        cancellationToken.ThrowIfCancellationRequested();

        if (!await userManager.IsInRoleAsync(user, AdminRoleName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var addRoleResult = await userManager.AddToRoleAsync(user, AdminRoleName);
            if (!addRoleResult.Succeeded)
            {
                var roleAssignmentError = $"Failed to assign '{AdminRoleName}' role to bootstrap admin user: {string.Join("; ", addRoleResult.Errors.Select(e => e.Description))}";
                var roleAssignmentException = new InvalidOperationException(roleAssignmentError);
                var rollbackResult = await userManager.DeleteAsync(user);
                if (!rollbackResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Critical bootstrap failure: failed to rollback created bootstrap admin user: {string.Join("; ", rollbackResult.Errors.Select(e => e.Description))}",
                        roleAssignmentException);
                }

                logger.LogWarning("Failed to assign admin role to bootstrap user. Created user has been rolled back.");
                throw roleAssignmentException;
            }
        }

        logger.LogInformation("Bootstrap admin initialization completed.");
    }

    public SystemTask StopAsync(CancellationToken cancellationToken) => SystemTask.CompletedTask;
}
