using BLL;
using DAL.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using TaskManager.Hosting.Cloud;

namespace Notifier.Authentication;

public static class NotifierAuthenticationExtensions
{
    public const string Scheme = "TaskManager.BrowserOrBearer";
    public static IServiceCollection AddNotifierAuthentication(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddRepositories(configuration);
        services.AddUserIdentityService(configuration);
        services.AddTaskManagerDataProtection(configuration, environment);
        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = Scheme;
            options.DefaultChallengeScheme = Scheme;
        });
        authentication.AddPolicyScheme(Scheme, Scheme, options => options.ForwardDefaultSelector = context =>
        {
            var header = context.Request.Headers.Authorization.ToString();
            return header.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme : IdentityConstants.ApplicationScheme;
        });
        authentication.AddIdentityCookies();
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        var sessionMinutes = configuration.GetValue("BrowserAuth:SessionMinutes", 60);
        if (sessionMinutes is < 5 or > 1440) throw new InvalidOperationException("BrowserAuth:SessionMinutes is outside supported bounds.");
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-TaskManager.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(sessionMinutes);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        return services;
    }
}
