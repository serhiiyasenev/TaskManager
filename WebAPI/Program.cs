using Asp.Versioning;
using AutoMapper;
using BLL;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Interfaces.Analytics;
using BLL.Mapping;
using BLL.Services;
using BLL.Services.Analytics;
using BLL.Validators;
using DAL.Extensions;
using FluentValidation;
using FluentValidation.AspNetCore;
using HealthChecks.UI.Client;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi;
using Serilog;
using System.Text.Json.Serialization;
using WebAPI;
using WebAPI.Middleware;
using WebAPI.Services;
using WebAPI.Authentication;
using BLL.Authorization;
using TaskManager.Hosting;

Serilog.Debugging.SelfLog.Enable(msg => File.AppendAllText("serilog-selflog.txt", msg + Environment.NewLine));

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTrustedProxyForwarding(builder.Configuration);

builder.Host.UseSerilog((ctx, services, lc) =>
    lc.ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        // Framework request-start logging includes query strings (email/invite tokens).
        .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", Serilog.Events.LogEventLevel.Warning));

// Configure Options Pattern
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<PaginationOptions>(builder.Configuration.GetSection(PaginationOptions.SectionName));
builder.Services.Configure<BootstrapAdminOptions>(builder.Configuration.GetSection(BootstrapAdminOptions.SectionName));
builder.Services.Configure<ReminderOptions>(builder.Configuration.GetSection(ReminderOptions.SectionName));

// Add AutoMapper
builder.Services.AddSingleton<IMapper>(_ =>
{
    var mapperConfiguration = new MapperConfiguration(
        config => config.AddProfile<MappingProfile>(),
        NullLoggerFactory.Instance);

    return mapperConfiguration.CreateMapper();
});

// Add FluentValidation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterUserDtoValidator>();

builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<CustomExceptionFilterAttribute>();
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://localhost:7151")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddRepositories(builder.Configuration);
builder.Services.AddUserIdentityService(builder.Configuration);
builder.Services.AddBrowserAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
builder.Services.AddScoped<IUserAccess, DatabaseUserAccess>();

// Direct legacy service tests keep their constructors; production always
// requires the caller scope and cannot silently use the unscoped test path.
builder.Services.AddScoped<ITaskAnalyticsService>(sp => ActivatorUtilities.CreateInstance<TaskAnalyticsService>(sp, sp.GetRequiredService<IUserAccess>()));
builder.Services.AddScoped<IProjectAnalyticsService>(sp => ActivatorUtilities.CreateInstance<ProjectAnalyticsService>(sp, sp.GetRequiredService<IUserAccess>()));
builder.Services.AddScoped<ITeamAnalyticsService>(sp => ActivatorUtilities.CreateInstance<TeamAnalyticsService>(sp, sp.GetRequiredService<IUserAccess>()));
builder.Services.AddScoped<IUserAnalyticsService>(sp => ActivatorUtilities.CreateInstance<UserAnalyticsService>(sp, sp.GetRequiredService<IUserAccess>()));

builder.Services.AddScoped<IProjectsService>(sp => ActivatorUtilities.CreateInstance<ProjectsService>(sp, sp.GetRequiredService<IUserAccess>()));
builder.Services.AddScoped<ITasksService>(sp => ActivatorUtilities.CreateInstance<TasksService>(sp, sp.GetRequiredService<IUserAccess>(), sp.GetRequiredService<DAL.Context.TaskContext>()));
builder.Services.AddScoped<ITeamManagementService, TeamManagementService>();
builder.Services.AddScoped<ITeamsService>(sp => ActivatorUtilities.CreateInstance<TeamsService>(sp, sp.GetRequiredService<IUserAccess>(), sp.GetRequiredService<ITeamManagementService>()));
builder.Services.AddScoped<IUsersService>(sp => ActivatorUtilities.CreateInstance<UsersService>(sp, sp.GetRequiredService<IUserAccess>()));
builder.Services.AddSingleton<IQueueService, RabbitMqService>();

builder.Services.AddScoped<IAuthService>(sp => ActivatorUtilities.CreateInstance<AuthService>(sp, sp.GetRequiredService<IAccountEmailSender>()));
builder.Services.AddHostedService<BootstrapAdminHostedService>();
builder.Services.AddHostedService<TaskReminderScheduler>();

// Add API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;

});

// Add Health Checks
builder.Services.AddHealthChecks()
    .AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("DbConnection")!,
        name: "sql-server",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready", "db", "sql", "sqlserver"]);

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
    options.UseInlineDefinitionsForEnums();

    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TaskManager API",
        Version = "v1",
        Description = "Don't forget to update your 'ConnectionStrings'."
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

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            []
        }
    });
});

Serilog.Debugging.SelfLog.Enable(Console.Error);

var app = builder.Build();
app.UseTrustedProxyForwarding();

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

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
app.UseRateLimiter();

app.MapControllers();

// Map Health Check endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).RequireAuthorization("AdminOnly");

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).AllowAnonymous();

app.Run();

public partial class Program
{
}
