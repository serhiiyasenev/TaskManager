using BLL.Interfaces;
using BLL.Services;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using DAL.Extensions;
using WebAPI;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DbConnection");

var migrationAssembly = typeof(TaskContext).Assembly.GetName().Name;

builder.Services.AddDbContext<TaskContext>(options =>
    options.UseSqlServer(connectionString, opt => opt.MigrationsAssembly(migrationAssembly)));

builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<CustomExceptionFilterAttribute>();
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRepositories(builder.Configuration);

builder.Services.AddScoped<IDataProvider, DataProvider>();
builder.Services.AddScoped<IDataProcessingService, DataProcessingService>();
builder.Services.AddScoped<IProjectsService, ProjectsService>();
builder.Services.AddScoped<ITasksService, TasksService>();
builder.Services.AddScoped<ITeamsService, TeamsService>();
builder.Services.AddScoped<IUsersService, UsersService>();
builder.Services.AddScoped<IQueueService, RabbitMqService>();

// Identity Core
// builder.Services.AddAuthentication(...);
// builder.Services.AddAuthorization();

var app = builder.Build();

// ---- Middleware ----

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
