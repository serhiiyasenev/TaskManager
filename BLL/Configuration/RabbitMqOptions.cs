namespace BLL.Configuration;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";
    
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string QueueName { get; set; } = "TestQueue";
    public bool Durable { get; set; } = true;
    public int RetryAttempts { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 1000;
    
    public string GetConnectionString() => 
        $"amqp://{UserName}:{Password}@{HostName}:{Port}";
}
