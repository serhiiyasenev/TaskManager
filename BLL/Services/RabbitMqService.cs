using System.Text;
using BLL.Interfaces;
using RabbitMQ.Client;

namespace BLL.Services;

public class RabbitMqService : IQueueService
{
    public async Task<bool> PostValue(string message, CancellationToken ct = default)
    {
        try
        {
            var factory = new ConnectionFactory { Uri = new Uri("amqp://guest:guest@localhost:15672") };

            await using var connection = await factory.CreateConnectionAsync(ct);
            await using var channel = await connection.CreateChannelAsync(cancellationToken:ct);

            await channel.QueueDeclareAsync("TestQueue", durable: true, exclusive: false, autoDelete: false,
                arguments: null, cancellationToken: ct);

            var body = Encoding.UTF8.GetBytes(message);

            var props = new BasicProperties { DeliveryMode = DeliveryModes.Persistent };

            await channel.BasicPublishAsync(
                exchange: "",
                routingKey: "TestQueue",
                mandatory: false,
                basicProperties: props,
                body: body, cancellationToken: ct);

            return true;

        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nRabbitMqService: {ex.Message}");
            return false;
        }
    }
}