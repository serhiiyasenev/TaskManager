#nullable enable
namespace BLL.Services.AI;

public sealed class TaskExtractionProviderException(string safeCode, int? retryAfterSeconds = null)
    : Exception("The task extraction provider could not complete the request.")
{
    public string SafeCode { get; } = safeCode;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}
