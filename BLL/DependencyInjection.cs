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
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        })
            .AddRoles<IdentityRole<int>>()
            .AddSignInManager<SignInManager<User>>()
            .AddEntityFrameworkStores<TaskContext>()
            .AddDefaultTokenProviders();

        var jwtKey = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            throw new InvalidOperationException("JWT Key is not configured in appsettings.json");
        }

        // Use SHA256 hash of the key to ensure it's exactly 256 bits (32 bytes)
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(jwtKey));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.NameIdentifier
                };
            });

        services.AddAuthorizationBuilder().AddPolicy("AdminOnly", p => p.RequireRole("admin"));

        return services;
    }
}