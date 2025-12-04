using Microsoft.AspNetCore.SignalR;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace Notifier
{
    public class RabbitMqListenerService : BackgroundService
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<RabbitMqListenerService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IAsyncPolicy _retryPolicy;

        public RabbitMqListenerService(
            IHubContext<ChatHub> hubContext,
            ILogger<RabbitMqListenerService> logger,
            IConfiguration configuration)
        {
            _hubContext = hubContext;
            _logger = logger;
            _configuration = configuration;
            
            // Create retry policy for connection
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryForeverAsync(
                    retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 60)),
                    (exception, retryCount, timespan) =>
                    {
                        _logger.LogWarning(
                            exception,
                            "RabbitMQ connection failed. Retry {RetryCount} after {Delay}s",
                            retryCount,
                            timespan.TotalSeconds);
                    });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    var rabbitMqHost = _configuration["RabbitMQ:HostName"] ?? "localhost";
                    var rabbitMqPort = _configuration["RabbitMQ:Port"] ?? "5672";
                    var rabbitMqUser = _configuration["RabbitMQ:UserName"] ?? "guest";
                    var rabbitMqPassword = _configuration["RabbitMQ:Password"] ?? "guest";
                    var queueName = _configuration["RabbitMQ:QueueName"] ?? "TestQueue";

                    var connectionString = $"amqp://{rabbitMqUser}:{rabbitMqPassword}@{rabbitMqHost}:{rabbitMqPort}";
                    var factory = new ConnectionFactory { Uri = new Uri(connectionString) };

                    _logger.LogInformation("Connecting to RabbitMQ at {ConnectionString}", connectionString.Replace(rabbitMqPassword, "***"));

                    await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                    await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                    await channel.QueueDeclareAsync(
                        queue: queueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: null,
                        cancellationToken: stoppingToken);

                    _logger.LogInformation("Successfully connected to RabbitMQ and listening on queue {QueueName}", queueName);

                    var consumer = new AsyncEventingBasicConsumer(channel);

                    consumer.ReceivedAsync += async (_, ea) =>
                    {
                        try
                        {
                            var message = Encoding.UTF8.GetString(ea.Body.ToArray());
                            _logger.LogInformation("Received message from queue: {Message}", message);

                            await _hubContext.Clients.All.SendAsync(
                                "ReceiveMessage",
                                "RabbitMQ Default User",
                                $"Task \"{message}\" was marked as Done. Great job!",
                                cancellationToken: stoppingToken);

                            _logger.LogInformation("Message broadcasted via SignalR");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing message from RabbitMQ");
                        }
                    };

                    await channel.BasicConsumeAsync(
                        queue: queueName,
                        autoAck: true,
                        consumer: consumer,
                        cancellationToken: stoppingToken);

                    // Keep the service running
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }

                    _logger.LogInformation("RabbitMQ listener service stopping gracefully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fatal error in RabbitMQ listener service");
                    throw; // Let Polly retry
                }
            });
        }
    }
}
