using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Text;
using System.Text.Json;
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
    private static readonly TimeSpan DisposeLockTimeout = TimeSpan.FromSeconds(10);
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqService> _logger;
    private readonly IAsyncPolicy _resiliencePolicy;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private readonly HashSet<string> _declaredQueues = new(StringComparer.OrdinalIgnoreCase);
    private IConnectionFactory? _factory;
    private IConnection? _connection;
    private IChannel? _channel;
    private int _disposeSignaled;

    public RabbitMqService(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqService> logger)
        : this(options, logger, ResiliencePolicies.CreateRabbitMqPolicy(logger))
    {
    }

    public RabbitMqService(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqService> logger,
        IAsyncPolicy resiliencePolicy,
        IConnectionFactory? connectionFactory = null)
    {
        _factory = connectionFactory;
        _options = options.Value;
        _logger = logger;
        _resiliencePolicy = resiliencePolicy;
        _connectionString = _options.GetConnectionString();
    }

    public async Task<bool> PostValue(string message, string? queueName = null, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposeSignaled) != 0)
        {
            _logger.LogWarning("RabbitMqService is shutting down; publish request is rejected.");
            return false;
        }

        var targetQueue = string.IsNullOrWhiteSpace(queueName) ? _options.QueueName : queueName;
        var messageId = GetMessageId(message);
        try
        {
            await _resiliencePolicy.ExecuteAsync(async policyCt =>
            {
                await _channelLock.WaitAsync(policyCt);
                try
                {
                    if (Volatile.Read(ref _disposeSignaled) != 0)
                    {
                        _logger.LogWarning("RabbitMqService is shutting down; publish request is rejected.");
                        throw new ObjectDisposedException(nameof(RabbitMqService));
                    }

                    await EnsureChannelAsync(policyCt);
                    await EnsureQueueDeclaredAsync(targetQueue, policyCt);

                    var body = Encoding.UTF8.GetBytes(message);

                    var props = new BasicProperties
                    {
                        DeliveryMode = DeliveryModes.Persistent,
                        MessageId = messageId,
                        CorrelationId = messageId,
                        Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    };

                    using var confirmCts = CancellationTokenSource.CreateLinkedTokenSource(policyCt);
                    confirmCts.CancelAfter(TimeSpan.FromSeconds(30));
                    await _channel!.BasicPublishAsync(
                        exchange: "",
                        routingKey: targetQueue,
                        mandatory: true,
                        basicProperties: props,
                        body: body,
                        cancellationToken: confirmCts.Token);

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
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to RabbitMQ queue {QueueName} after all retries", targetQueue);
            return false;
        }
    }

    private static string GetMessageId(string message)
    {
        try
        {
            using var json = JsonDocument.Parse(message);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("CorrelationId", out var id)
                && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
                return id.GetString()!;
        }
        catch (JsonException) { /* Legacy plain-text messages remain supported. */ }
        return Guid.NewGuid().ToString("N");
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
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
    }

    private IConnectionFactory GetFactory()
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
        if (Interlocked.Exchange(ref _disposeSignaled, 1) != 0)
        {
            return;
        }

        var lockAcquired = false;
        try
        {
            using var timeoutCts = new CancellationTokenSource(DisposeLockTimeout);
            await _channelLock.WaitAsync(timeoutCts.Token);
            lockAcquired = true;

            await DisposeChannelAsync();
            await DisposeConnectionAsync();
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Timed out waiting for RabbitMQ publish lock during shutdown.");
        }
        finally
        {
            if (lockAcquired)
            {
                _channelLock.Release();
            }
        }
    }
}
