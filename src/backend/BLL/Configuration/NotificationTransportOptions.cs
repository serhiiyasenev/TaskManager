#nullable enable
namespace BLL.Configuration;

public sealed class NotificationTransportOptions
{
    public const string SectionName = "Notifications";
    public string Provider { get; set; } = "RabbitMQ";
    public void Validate()
    {
        if (Provider is not ("RabbitMQ" or "AzureServiceBus"))
            throw new InvalidOperationException("Notifications:Provider must be RabbitMQ or AzureServiceBus.");
    }
}
