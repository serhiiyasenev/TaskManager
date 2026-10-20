extern alias NotifierLib;
using System.Text;
using System.Text.Json;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Models.Messaging;
using BLL.Services.Notifications;
using DAL.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NotifierLib::Notifier;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Tests.Notifications;

// Relational SQL processing with mock broker channels. Actual RabbitMQ→SQL
// evidence is provided by the independent opt-in CI integration gate.
internal sealed class DurableListenerHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider _provider = null!;
    private readonly SemaphoreSlim _publisherLock = new(1, 1);
    public FixedClock Clock { get; } = new();
    public Recorder Hints { get; } = new();
    public Mock<IChannel> Consumer { get; } = new();
    public Mock<IChannel> Publisher { get; } = new();
    public Mock<IConnectionFactory> Factory { get; } = new();
    public List<string> PublishedQueues { get; } = [];
    public RabbitMqListenerService Listener { get; private set; } = null!;
    public NotificationDeliveryCoordinator Coordinator { get; private set; } = null!;
    public TaskContext Context() => new(new DbContextOptionsBuilder<TaskContext>().UseSqlite(_connection).Options);

    public static async Task<DurableListenerHarness> CreateAsync(TaskCompletionSource<AsyncEventingBasicConsumer>? ready = null)
    {
        var h = new DurableListenerHarness(); await h._connection.OpenAsync();
        await using (var db = h.Context())
        {
            await db.Database.EnsureCreatedAsync();
            db.Teams.Add(new DAL.Entities.Team { Id = 1, Name = "Synthetic team", CreatedAt = h.Clock.GetUtcNow().UtcDateTime });
            db.Users.Add(new DAL.Entities.User("recipient", "Synthetic", "Member", "recipient@example.test")
                { Id = 7, TeamId = 1, EmailConfirmed = true, SecurityStamp = "synthetic-stamp" });
            db.Projects.Add(new DAL.Entities.Project { Id = 1, AuthorId = 7, TeamId = 1, Name = "Private project", Description = "Synthetic", CreatedAt = h.Clock.GetUtcNow().UtcDateTime });
            db.Tasks.Add(new DAL.Entities.Task { Id = 99, ProjectId = 1, PerformerId = 7, Name = "Private task", Description = "Synthetic",
                CreatedAt = h.Clock.GetUtcNow().UtcDateTime, DueDate = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(5) });
            await db.SaveChangesAsync();
        }
        var services = new ServiceCollection().AddScoped(_ => h.Context())
            .AddScoped<INotificationMessageProcessor>(provider => new NotificationMessageProcessor(
                provider.GetRequiredService<TaskContext>(), h.Clock, NullLogger<NotificationMessageProcessor>.Instance));
        h._provider = services.BuildServiceProvider();
        h.Coordinator = new(h._provider.GetRequiredService<IServiceScopeFactory>(), h.Hints, NullLogger<NotificationDeliveryCoordinator>.Instance);
        h.Consumer.SetupGet(channel => channel.IsOpen).Returns(true);
        h.Publisher.SetupGet(channel => channel.IsOpen).Returns(true);
        h.Consumer.Setup(channel => channel.QueueDeclareAsync(It.IsAny<string>(), true, false, false,
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string queue, bool _, bool _, bool _, IDictionary<string, object?>? _, bool _, bool _, CancellationToken _) => new QueueDeclareOk(queue, 0, 0));
        h.Consumer.Setup(channel => channel.BasicConsumeAsync(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object?>?, IAsyncBasicConsumer, CancellationToken>(
                (_, _, _, _, _, _, consumer, _) => ready?.TrySetResult((AsyncEventingBasicConsumer)consumer)).ReturnsAsync("synthetic-consumer");
        h.Publisher.Setup(channel => channel.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<BasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string queue, bool _, BasicProperties _, ReadOnlyMemory<byte> _, CancellationToken _) =>
            { h.PublishedQueues.Add(queue); return ValueTask.CompletedTask; });
        var connection = new Mock<IConnection>(); connection.SetupGet(value => value.IsOpen).Returns(true);
        connection.Setup(value => value.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateChannelOptions? channelOptions, CancellationToken _) => channelOptions?.PublisherConfirmationsEnabled == true ? h.Publisher.Object : h.Consumer.Object);
        connection.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        h.Factory.Setup(value => value.CreateConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        h.Listener = new(h.Coordinator, NullLogger<RabbitMqListenerService>.Instance,
            Options.Create(new RabbitMqOptions { QueueName = "TestQueue", ReminderQueueName = "TaskReminders" }), h.Factory.Object);
        return h;
    }

    public async Task<TaskNotificationEnvelope> IntentAsync(NotificationEventType type = NotificationEventType.TaskReminder)
    {
        var id = Guid.NewGuid(); await using var db = Context(); var task = await db.Tasks.SingleAsync();
        var envelope = new TaskNotificationEnvelope { Type = type, CorrelationId = id.ToString("N") };
        switch (type)
        {
            case NotificationEventType.ExecutedTaskCompleted:
                task.State = DAL.Enum.TaskState.Done;
                db.ExecutedTasks.Add(new DAL.Entities.ExecutedTask
                {
                    TaskId = task.Id, TaskName = task.Name, CreatedAt = Clock.GetUtcNow().UtcDateTime,
                    NotificationMessageId = id
                });
                envelope.ExecutedTask = new(99, task.Name, Clock.GetUtcNow().UtcDateTime, 7, 1, "Private project"); break;
            case NotificationEventType.TaskCreated:
                envelope.TaskCreated = new(id, 99, task.Name, 7, 1, "Private project", Clock.GetUtcNow().UtcDateTime); break;
            default:
                var escalation = type == NotificationEventType.TaskOverdueEscalation;
                task.DueDate = Clock.GetUtcNow().UtcDateTime.AddMinutes(escalation ? -30 : 5);
                task.ReminderEnabled = !escalation; task.EscalationEnabled = escalation;
                task.ReminderOffsetMinutes = 10; task.EscalationDelayMinutes = 20;
                if (escalation) task.EscalationNotificationId = id; else task.ReminderNotificationId = id;
                envelope.Reminder = new(99, task.Name, 7, 1, "Private project", task.DueDate.Value, 10, 20); break;
        }
        db.NotificationOutbox.Add(new DAL.Entities.NotificationOutboxMessage
        {
            Id = id, TaskId = 99, IsEscalation = type == NotificationEventType.TaskOverdueEscalation,
            Kind = type switch { NotificationEventType.TaskCreated => DAL.Entities.NotificationOutboxKind.TaskCreated,
                NotificationEventType.ExecutedTaskCompleted => DAL.Entities.NotificationOutboxKind.ExecutedTaskCompleted,
                _ => DAL.Entities.NotificationOutboxKind.Reminder },
            QueueName = type is NotificationEventType.TaskReminder or NotificationEventType.TaskOverdueEscalation ? "TaskReminders" : "TestQueue",
            Payload = JsonSerializer.Serialize(envelope), CreatedAtUtc = Clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(); return envelope;
    }

    public Task DeliverAsync(TaskNotificationEnvelope envelope, ulong tag = 1) => DeliverAsync(JsonSerializer.Serialize(envelope), envelope.CorrelationId, tag);
    public Task DeliverAsync(string payload, string? id, ulong tag = 1) => Listener.HandleDeliveryAsync("TestQueue",
        new BasicDeliverEventArgs("synthetic", tag, false, "", "TestQueue", new BasicProperties { MessageId = id, CorrelationId = id },
            Encoding.UTF8.GetBytes(payload), CancellationToken.None), Consumer.Object, Publisher.Object, _publisherLock, CancellationToken.None);
    public async ValueTask DisposeAsync() { Listener.Dispose(); await _provider.DisposeAsync(); _publisherLock.Dispose(); await _connection.DisposeAsync(); }

    internal sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
    }
    internal sealed class Recorder : IInboxRealtimePublisher
    {
        public List<(Guid Id, int Recipient)> Delivered { get; } = [];
        public Exception? Failure { get; set; }
        public Task PublishAsync(Guid notificationId, int recipientId, CancellationToken ct = default)
        { Delivered.Add((notificationId, recipientId)); return Failure is null ? Task.CompletedTask : Task.FromException(Failure); }
    }
}
