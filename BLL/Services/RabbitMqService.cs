using System.Diagnostics.CodeAnalysis;
using System.Text;
using BLL.Configuration;
using BLL.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RabbitMQ.Client;

namespace BLL.Services;

[ExcludeFromCodeCoverage]
public class RabbitMqService : IQueueService, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqService> _logger;
    private readonly IAsyncPolicy _resiliencePolicy;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private readonly HashSet<string> _declaredQueues = new(StringComparer.OrdinalIgnoreCase);
    private ConnectionFactory? _factory;
    private IConnection? _connection;
    private IChannel? _channel;

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
        _connectionString = _options.GetConnectionString();
    }

    public async Task<bool> PostValue(string message, string? queueName = null, CancellationToken ct = default)
    {
        var targetQueue = string.IsNullOrWhiteSpace(queueName) ? _options.QueueName : queueName;
        try
        {
            await _resiliencePolicy.ExecuteAsync(async policyCt =>
            {
                await _channelLock.WaitAsync(policyCt);
                try
                {
                    await EnsureChannelAsync(policyCt);
                    await EnsureQueueDeclaredAsync(targetQueue, policyCt);

                    var body = Encoding.UTF8.GetBytes(message);

                    var messageId = Guid.NewGuid().ToString();
                    var props = new BasicProperties
                    {
                        DeliveryMode = DeliveryModes.Persistent,
                        MessageId = messageId,
                        Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    };

                    await _channel!.BasicPublishAsync(
                        exchange: "",
                        routingKey: targetQueue,
                        mandatory: false,
                        basicProperties: props,
                        body: body,
                        cancellationToken: policyCt);

                    _logger.LogInformation(
                        "Message published to queue {QueueName} with ID {MessageId}",
                        targetQueue,
                        messageId);
                }
                finally
                {
                    _channelLock.Release();
                }
            }, ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to RabbitMQ queue {QueueName} after all retries", targetQueue);
            return false;
        }
    }

    private async Task EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel?.IsOpen == true && _connection?.IsOpen == true)
        {
            return;
        }

        await DisposeChannelAsync();
        await DisposeConnectionAsync();
        _declaredQueues.Clear();

        _connection = await GetFactory().CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);
    }

    private ConnectionFactory GetFactory()
    {
        if (_factory is not null)
        {
            return _factory;
        }

        try
        {
            _factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
            return _factory;
        }
        catch (UriFormatException ex)
        {
            throw new InvalidOperationException("Invalid RabbitMQ connection string", ex);
        }
    }

    private async Task EnsureQueueDeclaredAsync(string queueName, CancellationToken ct)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Channel is not initialized.");
        }

        if (_declaredQueues.Contains(queueName))
        {
            return;
        }

        await _channel.QueueDeclareAsync(
            queue: queueName,
            durable: _options.Durable,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        _declaredQueues.Add(queueName);
    }

    private async ValueTask DisposeChannelAsync()
    {
        if (_channel is IAsyncDisposable asyncDisposableChannel)
        {
            await asyncDisposableChannel.DisposeAsync();
        }
        else
        {
            _channel?.Dispose();
        }

        _channel = null;
    }

    private async ValueTask DisposeConnectionAsync()
    {
        if (_connection is IAsyncDisposable asyncDisposableConnection)
        {
            await asyncDisposableConnection.DisposeAsync();
        }
        else
        {
            _connection?.Dispose();
        }

        _connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _channelLock.WaitAsync();
        try
        {
            await DisposeChannelAsync();
            await DisposeConnectionAsync();
            _channelLock.Dispose();
        }
        finally
        {
            // no release after dispose
        }
    }
}
