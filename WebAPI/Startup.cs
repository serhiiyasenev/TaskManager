using System.Text.Json.Serialization;
using BLL.Interfaces;
using BLL.Services;
using DAL.Context;
using Microsoft.EntityFrameworkCore;

namespace WebAPI;

public class Startup(IConfiguration configuration)
{
    public IConfiguration Configuration { get; } = configuration;

    public void ConfigureServices(IServiceCollection services)
    {
        var migrationAssembly = typeof(TaskContext).Assembly.GetName().Name;

        services.AddDbContext<TaskContext>(options =>
            options.UseSqlServer(Configuration["ConnectionStrings:DbConnection"], opt => opt.MigrationsAssembly(migrationAssembly)));

        services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddControllers();

        services.AddScoped<IDataProvider, DataProvider>();
        services.AddScoped<IDataProcessingService, DataProcessingService>();
        services.AddScoped<IProjectsService, ProjectsService>();
        services.AddScoped<ITasksService, TasksService>();
        services.AddScoped<ITeamsService, TeamsService>();
        services.AddScoped<IUsersService, UsersService>();
        services.AddScoped<IQueueService, RabbitMqService>();

        services.AddMvcCore(options => options.Filters.Add(typeof(CustomExceptionFilterAttribute)));

        services.AddCors();

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseHttpsRedirection();

        app.UseCors(builder => builder
            .WithOrigins("http://localhost:4200")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());

        app.UseRouting();

        app.UseSwagger();
        app.UseSwaggerUI();

        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }

}