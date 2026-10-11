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
    public void Validate() => throw new NotImplementedException();
}
