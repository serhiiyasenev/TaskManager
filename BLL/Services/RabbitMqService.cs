using System.Text;
using BLL.Configuration;
using BLL.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RabbitMQ.Client;

namespace BLL.Services;

public class RabbitMqService : IQueueService
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqService> _logger;
    private readonly IAsyncPolicy _resiliencePolicy;

    public RabbitMqService(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqService> logger)
        : this(options, logger, ResiliencePolicies.CreateRabbitMqPolicy(logger))
    {
    }

    public RabbitMqService(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqService> logger,
        IAsyncPolicy resiliencePolicy)
    {
        _options = options.Value;
        _logger = logger;
        _resiliencePolicy = resiliencePolicy;
    }

    public async Task<bool> PostValue(string message, CancellationToken ct = default)
    {
        try
        {
            await _resiliencePolicy.ExecuteAsync(async policyCt =>
            {
                var factory = new ConnectionFactory { Uri = new Uri(_options.GetConnectionString()) };

                await using var connection = await factory.CreateConnectionAsync(policyCt);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: policyCt);

                await channel.QueueDeclareAsync(
                    _options.QueueName,
                    durable: _options.Durable,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: policyCt);

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
                    cancellationToken: policyCt);

                _logger.LogInformation(
                    "Message published to queue {QueueName} with ID {MessageId}",
                    _options.QueueName,
                    messageId);
            }, ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to RabbitMQ queue {QueueName} after all retries", _options.QueueName);
            return false;
        }
    }
}
