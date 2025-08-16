using BLL.Interfaces;
using BLL.Interfaces.Analytics;
using BLL.Services;
using BLL.Services.Queries;
using DAL.Extensions;
using System.Text.Json.Serialization;
using BLL;
using BLL.Mapping;
using WebAPI;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddAutoMapper(cfg =>
{
    cfg.AddProfile<MappingProfile>();
});

builder.Services.AddRepositories(builder.Configuration);
builder.Services.AddUserIdentityService(builder.Configuration);

builder.Services.AddScoped<ITaskAnalyticsService, TaskAnalyticsService>();
builder.Services.AddScoped<IProjectAnalyticsService, ProjectAnalyticsService>();
builder.Services.AddScoped<ITeamAnalyticsService, TeamAnalyticsService>();
builder.Services.AddScoped<IUserAnalyticsService, UserAnalyticsService>();

builder.Services.AddScoped<IProjectsService, ProjectsService>();
builder.Services.AddScoped<ITasksService, TasksService>();
builder.Services.AddScoped<ITeamsService, TeamsService>();
builder.Services.AddScoped<IUsersService, UsersService>();
builder.Services.AddScoped<IQueueService, RabbitMqService>();

builder.Services.AddScoped<IAuthService, AuthService>();

// Identity
// builder.Services.AddAuthentication(...);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("admin"));
});

builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
    options.UseInlineDefinitionsForEnums();

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter just your valid JWT token.\n\nExample: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();