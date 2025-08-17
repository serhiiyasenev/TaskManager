using BLL;
using BLL.Interfaces;
using BLL.Interfaces.Analytics;
using BLL.Services;
using BLL.Services.Analytics;
using DAL.Extensions;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DAL.Entities;
using WebAPI;
using WebAPI.Middleware;

Serilog.Debugging.SelfLog.Enable(msg => File.AppendAllText("serilog-selflog.txt", msg + Environment.NewLine));

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, lc) =>
    lc.ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services));

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

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
    options.UseInlineDefinitionsForEnums();

    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TaskManager API",
        Version = "v1",
        Description = "Don't forget to update your 'ConnectionStrings'" +
                      "\n\n Default admin credentials: **admin@example.com / Password1!**"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter just your valid JWT token.\n\nExample: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

Serilog.Debugging.SelfLog.Enable(Console.Error);

var app = builder.Build();

Log.Information("WebAPI started; env={Env}; service={Service}", "dev", "task-platform");

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
});

app.Use(async (ctx, next) =>
{
    const string header = "X-Correlation-ID";
    if (!ctx.Request.Headers.TryGetValue(header, out var cid) || string.IsNullOrWhiteSpace(cid))
    {
        cid = ctx.TraceIdentifier;
        ctx.Request.Headers[header] = cid;
    }
    ctx.Response.Headers[header] = cid;
    using (Serilog.Context.LogContext.PushProperty("CorrelationId", cid.ToString()))
    {
        await next();
    }
});

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseGlobalExceptionHandling();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();