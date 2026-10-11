#nullable enable
namespace BLL.Configuration;

public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";
    public string FullyQualifiedNamespace { get; set; } = "";
    public string? ManagedIdentityClientId { get; set; }
    public string QueueName { get; set; } = "TaskNotifications";
    public string? ReminderQueueName { get; set; }
    public int MaxConcurrentCalls { get; set; } = 1;
    public int TryTimeoutSeconds { get; set; } = 30;
    public int MaxAutoLockRenewalSeconds { get; set; } = 120;
    public void Validate()
    {
        if (FullyQualifiedNamespace.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(FullyQualifiedNamespace,
                "^[a-z][a-z0-9-]{1,48}[a-z0-9]\\.servicebus\\.windows\\.net$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            || (!string.IsNullOrWhiteSpace(ManagedIdentityClientId) && !Guid.TryParse(ManagedIdentityClientId, out _))
            || !ValidQueue(QueueName) || (ReminderQueueName is not null && !ValidQueue(ReminderQueueName))
            || MaxConcurrentCalls is < 1 or > 8 || TryTimeoutSeconds is < 1 or > 60
            || MaxAutoLockRenewalSeconds is < 60 or > 300)
            throw new InvalidOperationException("Service Bus notification configuration is invalid.");
    }

    private static bool ValidQueue(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 249
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ':');
}
