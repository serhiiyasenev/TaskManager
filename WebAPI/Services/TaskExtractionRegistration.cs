using System.Security.Claims;
using System.Threading.RateLimiting;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Services.AI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WebAPI.Controllers;

namespace WebAPI.Services;

internal static class TaskExtractionRegistration
{
    public static IServiceCollection AddTaskExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TaskExtractionOptions>().Bind(configuration.GetSection(TaskExtractionOptions.SectionName))
            .Validate(options =>
            {
                try { options.Validate(); return true; }
                catch (InvalidOperationException) { return false; }
            }, "AI configuration contains an unsupported provider or limit.")
            .Validate<IHostEnvironment>((options, environment) => options.Provider != "Fake" || environment.EnvironmentName == "Testing",
                "The Fake AI provider is permitted only in Testing.")
            .ValidateOnStart();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<ITaskExtractionChatTransport>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TaskExtractionOptions>>().Value;
            return options.Provider == "Azure" && !string.IsNullOrWhiteSpace(options.Endpoint) && !string.IsNullOrWhiteSpace(options.Deployment)
                ? new AzureSdkTaskExtractionChatTransport(options, provider.GetRequiredService<IHostEnvironment>())
                : new UnavailableTaskExtractionChatTransport();
        });
        services.AddSingleton<ITaskExtractionProvider>(provider => new AzureTaskExtractionProvider(
            provider.GetRequiredService<IOptions<TaskExtractionOptions>>().Value,
            provider.GetRequiredService<IHostEnvironment>(), provider.GetRequiredService<ITaskExtractionChatTransport>()));
        services.AddScoped<IAiTaskDraftService, AiTaskDraftService>();
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(AiTaskExtractionsController.RatePolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<TaskExtractionOptions>>().Value.RequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                }));
            options.OnRejected = async (context, ct) =>
            {
                if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName != AiTaskExtractionsController.RatePolicy) return;
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay))
                    response.Headers.RetryAfter = Math.Ceiling(delay.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                await response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = 429, Title = "rate_limited", Detail = "AI request rate reached. Retry after the window expires.",
                    Extensions = { ["traceId"] = context.HttpContext.TraceIdentifier }
                }, ct);
            };
        });
        return services;
    }
}
