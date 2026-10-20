using Microsoft.Extensions.Logging;
using Polly;

namespace BLL.Configuration;

public static class ResiliencePolicies
{
    /// <summary>
    /// Creates a retry policy for RabbitMQ operations with exponential backoff
    /// </summary>
    public static IAsyncPolicy CreateRabbitMqPolicy(ILogger logger)
    {
        return Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    logger.LogWarning(
                        "RabbitMQ operation failed. Retry {RetryCount} after {Delay}s",
                        retryCount,
                        timespan.TotalSeconds);
                });
    }
}
