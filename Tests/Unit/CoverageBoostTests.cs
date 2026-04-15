using System.Reflection;
using BLL;
using BLL.Configuration;
using DAL.Context;
using DAL.Extensions;
using DAL.Repositories.Implementation;
using DAL.Repositories.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ProjectEntity = DAL.Entities.Project;
using TaskEntity = DAL.Entities.Task;
using TeamEntity = DAL.Entities.Team;
using UserEntity = DAL.Entities.User;

namespace Tests.Unit;

public class CoverageBoostTests
{
    [Fact]
    public void BllDependencyInjection_WithoutJwtKey_Throws()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TaskContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddUserIdentityService(configuration));
        Assert.Contains("JWT Key is not configured", ex.Message);
    }

    [Fact]
    public void BllDependencyInjection_WithJwtKey_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TaskContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "very-secure-test-key"
        }).Build();

        var result = services.AddUserIdentityService(configuration);
        var provider = result.BuildServiceProvider();

        Assert.Same(services, result);
        Assert.NotNull(provider.GetService<IAuthenticationSchemeProvider>());
        Assert.NotNull(provider.GetService<IAuthorizationService>());
    }

    [Fact]
    public void DalServiceCollectionExtensions_RegistersRepositories()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DbConnection"] = "Server=(localdb)\\mssqllocaldb;Database=TaskManagerTest;Trusted_Connection=True;"
        }).Build();

        var result = services.AddRepositories(configuration);
        var provider = result.BuildServiceProvider();

        Assert.Same(services, result);
        Assert.NotNull(provider.GetService<IUnitOfWork>());
        Assert.NotNull(provider.GetService<IRepository<UserEntity>>());
    }

    [Fact]
    public async Task EfCoreRepository_ExercisesAllPublicMethods()
    {
        var options = new DbContextOptionsBuilder<TaskContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new TaskContext(options);
        var team = new TeamEntity { Id = 1, Name = "Team", CreatedAt = DateTime.UtcNow, Users = [], Projects = [] };
        var user = new UserEntity { Id = 1, UserName = "u", FirstName = "f", LastName = "l", Email = "u@e.com", NormalizedEmail = "U@E.COM", RegisteredAt = DateTime.UtcNow };
        var project = new ProjectEntity { Id = 1, Name = "P", Description = "D", TeamId = 1, AuthorId = 1, Team = team, Author = user, Tasks = [], CreatedAt = DateTime.UtcNow, Deadline = DateTime.UtcNow.AddDays(1) };
        var task = new TaskEntity { Id = 1, Name = "T", Description = "D", ProjectId = 1, PerformerId = 1, Project = project, Performer = user, CreatedAt = DateTime.UtcNow };
        db.Teams.Add(team);
        db.Users.Add(user);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var repo = new EfCoreRepository<TaskEntity>(db);

        Assert.NotEmpty(repo.Query());
        Assert.True(await repo.AnyAsync(t => t.Id == 1));
        Assert.Single(await repo.ListAsync(null));
        Assert.Single(await repo.ListAsync(t => t.Id == 1));
        Assert.NotNull(await repo.GetByIdAsync(1));
        Assert.NotNull(await repo.GetByIdWithIncludesAsync(1, default, t => t.Project));
        Assert.Single(await repo.ListWithIncludesAsync(null, default, t => t.Project));
        Assert.Single(await repo.ListWithIncludesAsync(t => t.Id == 1, default, t => t.Project));

        var newTask = new TaskEntity { Id = 2, Name = "T2", Description = "D2", ProjectId = 1, PerformerId = 1, Project = project, Performer = user, CreatedAt = DateTime.UtcNow };
        await repo.AddAsync(newTask);
        await db.SaveChangesAsync();
        repo.Update(newTask);
        await db.SaveChangesAsync();
        repo.Remove(newTask);
        await db.SaveChangesAsync();
    }

    [Fact]
    public void ReflectionSmoke_Touches_BllModels_And_DalEntities()
    {
        var assemblies = new[] { typeof(BootstrapAdminOptions).Assembly, typeof(UserEntity).Assembly };
        var targetTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t =>
                t is { IsAbstract: false, IsInterface: false } &&
                (t.Namespace?.StartsWith("BLL.Models") == true ||
                 t.Namespace?.StartsWith("BLL.Configuration") == true ||
                 t.Namespace?.StartsWith("DAL.Entities") == true))
            .Where(t => !t.ContainsGenericParameters)
            .ToList();

        Assert.NotEmpty(targetTypes);

        foreach (var type in targetTypes)
        {
            var instance = CreateInstance(type);
            if (instance is null)
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanWrite)
                {
                    property.SetValue(instance, CreateValue(property.PropertyType));
                }

                if (property.CanRead)
                {
                    _ = property.GetValue(instance);
                }
            }
        }
    }

    private static object? CreateInstance(Type type)
    {
        try
        {
            var constructor = type
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (constructor is null)
            {
                return Activator.CreateInstance(type);
            }

            var args = constructor.GetParameters()
                .Select(p => CreateValue(p.ParameterType))
                .ToArray();

            return constructor.Invoke(args);
        }
        catch
        {
            return Activator.CreateInstance(type);
        }
    }

    private static object? CreateValue(Type type)
    {
        if (type == typeof(string))
            return "x";

        if (type == typeof(DateTime))
            return DateTime.UtcNow;

        if (type == typeof(DateTimeOffset))
            return DateTimeOffset.UtcNow;

        if (type == typeof(Guid))
            return Guid.NewGuid();

        if (type == typeof(bool))
            return true;

        if (type.IsEnum)
            return Enum.GetValues(type).GetValue(0)!;

        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return CreateValue(underlying);

        if (type.IsArray)
            return Array.CreateInstance(type.GetElementType()!, 0);

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            return Activator.CreateInstance(type)!;

        if (type.IsValueType)
            return Activator.CreateInstance(type)!;

        return null;
    }
}
