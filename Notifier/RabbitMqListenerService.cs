using Microsoft.AspNetCore.SignalR;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using BLL.Models.Messaging;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Notifier
{
    public class RabbitMqListenerService : BackgroundService
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<RabbitMqListenerService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IConnectionFactory? _connectionFactory;
        private readonly IServiceScopeFactory? _scopeFactory;
        private readonly IAsyncPolicy _retryPolicy;
        private readonly HashSet<string> _queueNames;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public RabbitMqListenerService(
            IHubContext<ChatHub> hubContext,
            ILogger<RabbitMqListenerService> logger,
            IConfiguration configuration,
            IConnectionFactory? connectionFactory = null,
            IServiceScopeFactory? scopeFactory = null)
        {
            _hubContext = hubContext;
            _logger = logger;
            _configuration = configuration;
            _connectionFactory = connectionFactory;
            _scopeFactory = scopeFactory;
            
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

            var mainQueue = _configuration["RabbitMQ:QueueName"] ?? "TestQueue";
            var reminderQueue = _configuration["RabbitMQ:ReminderQueueName"];
            _queueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mainQueue };
            if (!string.IsNullOrWhiteSpace(reminderQueue))
            {
                _queueNames.Add(reminderQueue);
            }
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

                    var connectionString = $"amqp://{rabbitMqUser}:{rabbitMqPassword}@{rabbitMqHost}:{rabbitMqPort}";
                    var factory = _connectionFactory ?? new ConnectionFactory { Uri = new Uri(connectionString) };

                    _logger.LogInformation("Connecting to RabbitMQ at {ConnectionString}", connectionString.Replace(rabbitMqPassword, "***"));

                    await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                    await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                    foreach (var queueName in _queueNames)
                    {
                        await channel.QueueDeclareAsync(
                            queue: queueName,
                            durable: true,
                            exclusive: false,
                            autoDelete: false,
                            arguments: null,
                            cancellationToken: stoppingToken);
                        _logger.LogInformation("Listening on queue {QueueName}", queueName);
                    }

                    await channel.BasicQosAsync(0, 1, false, stoppingToken);
                    foreach (var queueName in _queueNames)
                    {
                        var consumer = new AsyncEventingBasicConsumer(channel);
                        consumer.ReceivedAsync += async (_, ea) =>
                        {
                            try
                            {
                                await HandleMessageAsync(queueName, ea, stoppingToken);
                                await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                // Closing the channel requeues this unacknowledged delivery.
                            }
                            catch (InvalidDataException)
                            {
                                _logger.LogWarning("Rejecting unrouteable notification delivery {DeliveryTag}", ea.DeliveryTag);
                                // Permanent invalid payloads go to a configured DLX, not an infinite retry loop.
                                await channel.BasicNackAsync(ea.DeliveryTag, false, false, stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Requeueing failed delivery {DeliveryTag}", ea.DeliveryTag);
                                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                                await channel.BasicNackAsync(ea.DeliveryTag, false, true, stoppingToken);
                            }
                        };

                        await channel.BasicConsumeAsync(
                            queue: queueName,
                            autoAck: false,
                            consumer: consumer,
                            cancellationToken: stoppingToken);
                    }

                    // Keep the service running
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }

                    _logger.LogInformation("RabbitMQ listener service stopping gracefully");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("RabbitMQ listener service stopping due to cancellation");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fatal error in RabbitMQ listener service");
                    throw; // Let Polly retry
                }
            });
        }

        internal Task HandleMessageAsyncForTests(string queueName, string message, CancellationToken token) =>
            HandleMessageAsync(queueName, new BasicDeliverEventArgs(
                consumerTag: string.Empty,
                deliveryTag: 1,
                redelivered: false,
                exchange: string.Empty,
                routingKey: queueName,
                properties: new BasicProperties(),
                body: new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(message)),
                cancellationToken: token), token);

        private async Task HandleMessageAsync(string queueName, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
        {
            try
            {
                var message = Encoding.UTF8.GetString(ea.Body.Span);
                _logger.LogInformation("Received notification from queue {QueueName}", queueName);

                var envelope = DeserializeEnvelope(message);
                if (envelope is not null)
                {
                    var recipientId = await ResolveRecipientAsync(envelope, stoppingToken);
                    var (user, text) = BuildNotification(envelope);
                    if (envelope.Type is NotificationEventType.TaskReminder or NotificationEventType.TaskOverdueEscalation)
                    {
                        _logger.LogInformation("Simulated email notification to performer {PerformerId}", envelope.Reminder?.PerformerId);
                    }
                    await _hubContext.Clients.User(recipientId.ToString(CultureInfo.InvariantCulture)).SendAsync(
                        "ReceiveMessage",
                        user,
                        text,
                        cancellationToken: stoppingToken);

                    _logger.LogInformation("SignalR send completed for {Type}", envelope.Type);
                    return;
                }

                throw new InvalidDataException("A structured notification with a resolvable recipient is required.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from RabbitMQ queue {QueueName}", queueName);
                throw;
            }
        }

        private async Task<int> ResolveRecipientAsync(TaskNotificationEnvelope envelope, CancellationToken token)
        {
            if (envelope.Type is NotificationEventType.TaskReminder or NotificationEventType.TaskOverdueEscalation)
            {
                if (envelope.Reminder is { PerformerId: > 0 } reminder) return reminder.PerformerId;
                throw new InvalidDataException("A reminder requires a positive performer ID.");
            }
            if (envelope.Type == NotificationEventType.ExecutedTaskCompleted && envelope.ExecutedTask is { TaskId: > 0 } executed)
            {
                if (_scopeFactory is null) throw new InvalidDataException("Task recipient lookup is unavailable.");
                using var scope = _scopeFactory.CreateScope();
                var database = scope.ServiceProvider.GetRequiredService<TaskContext>();
                var recipient = await database.Tasks.AsNoTracking().Where(t => t.Id == executed.TaskId)
                    .Select(t => t.PerformerId).FirstOrDefaultAsync(token);
                if (recipient > 0) return recipient;
                throw new InvalidDataException("The task has no resolvable notification recipient.");
            }
            throw new InvalidDataException("The notification type or payload is unsupported.");
        }

        private TaskNotificationEnvelope? DeserializeEnvelope(string message)
        {
            try
            {
                return JsonSerializer.Deserialize<TaskNotificationEnvelope>(message, _serializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Failed to parse message as TaskNotificationEnvelope");
                return null;
            }
        }

        internal static (string User, string Message) BuildNotification(TaskNotificationEnvelope envelope)
        {
            return envelope.Type switch
            {
                NotificationEventType.ExecutedTaskCompleted when envelope.ExecutedTask is not null =>
                    ("Notifier",
                        $"Task \"{envelope.ExecutedTask.TaskName}\" was marked as Done at {envelope.ExecutedTask.CompletedAtUtc:u}. Great job!"),
                NotificationEventType.TaskReminder when envelope.Reminder is not null =>
                    ("Reminder",
                        $"Upcoming task \"{envelope.Reminder.TaskName}\" for performer {envelope.Reminder.PerformerId} due at {envelope.Reminder.DueDateUtc:u} (project: {envelope.Reminder.ProjectName ?? "N/A"})."),
                NotificationEventType.TaskOverdueEscalation when envelope.Reminder is not null =>
                    ("Overdue",
                        $"Task \"{envelope.Reminder.TaskName}\" assigned to performer {envelope.Reminder.PerformerId} is OVERDUE since {envelope.Reminder.DueDateUtc:u}. Please take action."),
                _ => ("RabbitMQ Default User", "Received an unrecognized notification payload.")
            };
        }
    }
}
