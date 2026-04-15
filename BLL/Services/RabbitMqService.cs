using System.Text;
using BLL.Configuration;
using BLL.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RabbitMQ.Client;

namespace BLL.Services;

public class RabbitMqService(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqService> logger,
    IAsyncPolicy? resiliencePolicy = null)
    : IQueueService
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly IAsyncPolicy _resiliencePolicy = resiliencePolicy ?? ResiliencePolicies.CreateRabbitMqPolicy(logger);

    public async Task<bool> PostValue(string message, CancellationToken ct = default)
    {
        try
        {
            await _resiliencePolicy.ExecuteAsync(async cancellationToken =>
            {
                var factory = new ConnectionFactory { Uri = new Uri(_options.GetConnectionString()) };

                await using var connection = await factory.CreateConnectionAsync(cancellationToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

                await channel.QueueDeclareAsync(
                    _options.QueueName,
                    durable: _options.Durable,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: cancellationToken);

                var body = Encoding.UTF8.GetBytes(message);

                var messageId = Guid.NewGuid().ToString();
                var props = new BasicProperties
                {
                    DeliveryMode = DeliveryModes.Persistent,
                    MessageId = messageId,
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                };

                await channel.BasicPublishAsync(
                    exchange: "",
                    routingKey: _options.QueueName,
                    mandatory: false,
                    basicProperties: props,
                    body: body,
                    cancellationToken: cancellationToken);

                logger.LogInformation(
                    "Message published to queue {QueueName} with ID {MessageId}",
                    _options.QueueName,
                    messageId);
            }, ct);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish message to RabbitMQ queue {QueueName} after all retries", _options.QueueName);
            return false;
        }
    }
}
