using DAL.Context;
using DAL.Repositories.Base;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DAL.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRepositories(this IServiceCollection services, IConfiguration configuration)
    {
        var migrationsAssembly = typeof(TaskContext).Assembly.GetName().Name;

        services.AddDbContext<TaskContext>(options =>
            options.UseSqlServer(
                configuration["ConnectionStrings:DbConnection"],
                sql => sql.MigrationsAssembly(migrationsAssembly)));

        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

        services.AddScoped<DbContext, TaskContext>();

        return services;
    }
}