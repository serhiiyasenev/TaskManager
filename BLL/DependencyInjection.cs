using DAL.Context;
using DAL.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace BLL;

public static class DependencyInjection
{
    public static IServiceCollection AddUserIdentityService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentityCore<User>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 8;
            options.User.RequireUniqueEmail = true;
            options.SignIn.RequireConfirmedEmail = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        })
            .AddRoles<IdentityRole<int>>()
            .AddSignInManager<SignInManager<User>>()
            .AddEntityFrameworkStores<TaskContext>()
            .AddDefaultTokenProviders();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var jwtKey = configuration["Jwt:Key"];
                if (string.IsNullOrWhiteSpace(jwtKey) ||
                    jwtKey.TrimStart().StartsWith("@Microsoft.KeyVault", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Jwt:Key must be configured through private configuration.");
                }
                // Resolve final configuration at startup (including host/test
                // providers), preserving the console client's signing contract.
                var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(jwtKey));
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = configuration["Jwt:Audience"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.NameIdentifier
                };
            });
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).ValidateOnStart();

        services.AddAuthorizationBuilder().AddPolicy("AdminOnly", p => p.RequireRole("admin"));

        return services;
    }
}
