using System.Threading.RateLimiting;
using System.Net.Mail;
using BLL.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace WebAPI.Authentication;

public static class BrowserAuthenticationExtensions
{
    public const string Scheme = "TaskManager.BrowserOrBearer";
    public const string CookieName = "__Host-TaskManager.Session";
    public const string DataProtectionApplicationName = "TaskManager";
    public const string AuthRateLimitPolicy = "account-authentication";
    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";

    public static IServiceCollection AddBrowserAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // Validate final host configuration at startup. WebApplicationFactory and
        // deployed hosts may append providers after service registration.
        services.AddOptions<BrowserAuthenticationOptions>()
            .Bind(configuration.GetSection(BrowserAuthenticationOptions.SectionName))
            .Validate(options => HasValidPublicOrigin(options.PublicOrigin, environment), "BrowserAuth:PublicOrigin is invalid.")
            .Validate(options => options.SessionMinutes is >= 5 and <= 1440 &&
                                 options.AuthRequestsPerMinute is >= 5 and <= 1000,
                "BrowserAuth session/rate limits are outside supported bounds.")
            .Validate(_ => !IsProduction(environment) || !string.IsNullOrWhiteSpace(configuration["BrowserAuth:PublicOrigin"]),
                "BrowserAuth:PublicOrigin must be explicitly configured in production.")
            .Validate(_ => string.IsNullOrWhiteSpace(configuration["DataProtection:Provider"]) ||
                           configuration["DataProtection:Provider"] == "FileSystem",
                "DataProtection:Provider is not implemented. Configure a persistent FileSystem key ring.")
            .Validate(_ => !IsProduction(environment) ||
                           (!string.IsNullOrWhiteSpace(configuration["DataProtection:KeyRingPath"]) &&
                            Path.IsPathFullyQualified(configuration["DataProtection:KeyRingPath"]!)),
                "Production requires an absolute persistent DataProtection:KeyRingPath.")
            .ValidateOnStart();
        services.AddOptions<AccountEmailOptions>()
            .Bind(configuration.GetSection(AccountEmailOptions.SectionName))
            .Validate(options => options.SmtpPort is >= 1 and <= 65535 && !string.IsNullOrWhiteSpace(options.SmtpHost),
                "Email SMTP host/port is invalid.")
            .Validate(options => IsValidEmailAddress(options.FromAddress), "Email:FromAddress is invalid.")
            .Validate(options => !IsProduction(environment) ||
                                  (!string.IsNullOrWhiteSpace(configuration["Email:SmtpHost"]) &&
                                   !string.IsNullOrWhiteSpace(configuration["Email:FromAddress"]) && options.UseTls),
                "Production email requires explicit SMTP host/sender and TLS.")
            .ValidateOnStart();
        services.AddDataProtection().SetApplicationName(DataProtectionApplicationName);
        services.AddOptions<KeyManagementOptions>().Configure<ILoggerFactory>((options, loggerFactory) =>
        {
            var keyRingPath = configuration["DataProtection:KeyRingPath"];
            if (!string.IsNullOrWhiteSpace(keyRingPath))
            {
                options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keyRingPath), loggerFactory);
            }
        });
        services.Configure<IdentityOptions>(options => options.SignIn.RequireConfirmedEmail = true);
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IAccountEmailSender, SmtpAccountEmailSender>();
        services.AddScoped<BrowserAntiforgeryFilter>();
        services.Configure<MvcOptions>(options => options.Filters.AddService<BrowserAntiforgeryFilter>());

        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultScheme = Scheme;
            options.DefaultAuthenticateScheme = Scheme;
            options.DefaultChallengeScheme = Scheme;
            options.DefaultForbidScheme = Scheme;
            options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
        }).AddPolicyScheme(Scheme, Scheme, options =>
        {
            options.ForwardDefaultSelector = context => HasBearerHeader(context.Request)
                ? JwtBearerDefaults.AuthenticationScheme : IdentityConstants.ApplicationScheme;
        });
        authentication.AddIdentityCookies();
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IOptions<BrowserAuthenticationOptions>, TimeProvider>((options, account, clock) =>
        {
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(account.Value.SessionMinutes);
            options.TimeProvider = clock;
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
        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = "__Host-TaskManager.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });
        services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().Build());
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<BrowserAuthenticationOptions>>().Value.AuthRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        });
        return services;
    }

    public static bool HasBearerHeader(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
            header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
    }

    internal static Uri ValidatePublicOrigin(string value, IHostEnvironment environment)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) ||
            !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != "/" ||
            (origin.Scheme != Uri.UriSchemeHttps &&
             !(origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback &&
               (environment.IsDevelopment() || environment.IsEnvironment("Testing")))))
        {
            throw new InvalidOperationException("BrowserAuth:PublicOrigin must be an HTTPS origin without path, credentials, query or fragment.");
        }
        return origin;
    }

    private static bool IsProduction(IHostEnvironment environment) =>
        !environment.IsDevelopment() && !environment.IsEnvironment("Testing");

    private static bool HasValidPublicOrigin(string value, IHostEnvironment environment)
    {
        try { ValidatePublicOrigin(value, environment); return true; }
        catch (InvalidOperationException) { return false; }
    }

    private static bool IsValidEmailAddress(string value)
    {
        try { _ = new MailAddress(value); return true; }
        catch (Exception exception) when (exception is FormatException or ArgumentException) { return false; }
    }
}
