using System.Text;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Services;
using BLL.Services.Notifications;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace Tests.Notifications;

public sealed class RabbitSqlDeliveryTests
{
    [RabbitSqlFact]
    public async Task RealBrokerAndMigratedSql_RecoverAckLossRetryTtlAndBoundedDeadLetter()
    {
        await using var database = await NotificationTestDatabase.CreateAsync();
        var fixture = await SqlServerNotificationsConcurrencyTests.CreateFixtureAsync(database);
        using var factory = fixture.Factory;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = deadline.Token;
        var queue = "taskmanager-ci-" + Guid.NewGuid().ToString("N");
        var options = new RabbitMqOptions { HostName = "localhost", Port = 5672, UserName = "taskmanager-ci",
            Password = "TaskManager_CI_Rabbit_Disposable", QueueName = queue, RetryDelayMilliseconds = 100 };
        var broker = new ConnectionFactory { HostName = options.HostName, UserName = options.UserName, Password = options.Password,
            AutomaticRecoveryEnabled = false, RequestedConnectionTimeout = TimeSpan.FromSeconds(5) };
        await using var connection = await broker.CreateConnectionAsync(ct);
        await using var publisher = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
        await RabbitNotificationTopology.DeclareAsync(publisher, queue, options, ct);
        using var publishLock = new SemaphoreSlim(1, 1);
        var hints = new Hints();
        var coordinator = new NotificationDeliveryCoordinator(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            hints, NullLogger<NotificationDeliveryCoordinator>.Instance);
        try
        {
            // The real application sender awaits mandatory publisher confirms.
            await using var sender = new RabbitMqService(Options.Create(options), NullLogger<RabbitMqService>.Instance);
            Assert.True(await sender.PostValue(fixture.Payload, queue, ct));
            await using (var first = await connection.CreateChannelAsync(cancellationToken: ct))
            {
                var received = await ReceiveAsync(first, queue, ct);
                var settled = Settlement(first, publisher, publishLock, queue, received, ct);
                await coordinator.ProcessAsync(Delivery(received), new DropAcknowledgement(settled), ct);
                Assert.Equal(1, hints.Count);
                await AssertOneSqlNotificationAsync(fixture);
                // Closing the channel loses its ACK and causes actual redelivery.
                await first.CloseAsync(cancellationToken: ct);
            }
            await using var consumer = await connection.CreateChannelAsync(cancellationToken: ct);
            var duplicate = await ReceiveAsync(consumer, queue, ct);
            Assert.True(duplicate.Redelivered);
            await coordinator.ProcessAsync(Delivery(duplicate), Settlement(consumer, publisher, publishLock, queue, duplicate, ct), ct);
            Assert.Equal(1, hints.Count);
            await AssertOneSqlNotificationAsync(fixture);
            Assert.Null(await consumer.BasicGetAsync(queue, true, ct));

            // Inject one explicit processing failure, use actual quorum TTL/DLX,
            // then run the real SQL processor. This is not a claimed SQL outage.
            var faults = new Faults { Remaining = 1 };
            await using var retryServices = new ServiceCollection().AddDbContext<TaskContext>(builder => builder.UseSqlServer(database.ConnectionString))
                .AddScoped<INotificationMessageProcessor>(provider => new ControlledProcessor(provider.GetRequiredService<TaskContext>(), fixture.Clock, faults))
                .BuildServiceProvider();
            var retry = new NotificationDeliveryCoordinator(retryServices.GetRequiredService<IServiceScopeFactory>(), hints,
                NullLogger<NotificationDeliveryCoordinator>.Instance);
            Assert.True(await sender.PostValue(fixture.Payload, queue, ct));
            var attemptOne = await ReceiveAsync(consumer, queue, ct);
            await retry.ProcessAsync(Delivery(attemptOne), Settlement(consumer, publisher, publishLock, queue, attemptOne, ct), ct);
            var attemptTwo = await ReceiveAsync(consumer, queue, ct);
            Assert.Equal(2, NotificationDeliveryMetadata.GetRabbitAttempt(attemptTwo.BasicProperties.Headers));
            Assert.Equal(attemptOne.BasicProperties.MessageId, attemptTwo.BasicProperties.MessageId);
            Assert.Equal(attemptOne.Body.ToArray(), attemptTwo.Body.ToArray());
            await retry.ProcessAsync(Delivery(attemptTwo), Settlement(consumer, publisher, publishLock, queue, attemptTwo, ct), ct);
            Assert.Equal(2, faults.Calls);
            Assert.Equal(1, hints.Count);
            await AssertOneSqlNotificationAsync(fixture);

            // Three failed invocations in this delivery lineage end in durable
            // quarantine. Stable identity/body survive both retries.
            faults.Calls = 0; faults.Remaining = 3;
            Assert.True(await sender.PostValue(fixture.Payload, queue, ct));
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var received = await ReceiveAsync(consumer, queue, ct);
                Assert.Equal(attempt, NotificationDeliveryMetadata.GetRabbitAttempt(received.BasicProperties.Headers));
                await retry.ProcessAsync(Delivery(received), Settlement(consumer, publisher, publishLock, queue, received, ct), ct);
            }
            var dead = await ReceiveAsync(consumer, queue + ".dead", ct);
            Assert.Equal(fixture.MessageId.ToString("N"), dead.BasicProperties.MessageId);
            Assert.Equal(fixture.Payload, Encoding.UTF8.GetString(dead.Body.Span));
            Assert.Equal("AttemptsExhausted", Header(dead, "x-taskmanager-failure"));
            await consumer.BasicAckAsync(dead.DeliveryTag, false, ct);
            Assert.Equal(3, faults.Calls);
            Assert.Null(await consumer.BasicGetAsync(queue, true, ct));
            Assert.Null(await consumer.BasicGetAsync(queue + ".retry", true, ct));
            Assert.Equal(1, hints.Count);
            await AssertOneSqlNotificationAsync(fixture);
        }
        finally
        {
            // Delete only the freshly generated test queues in this disposable
            // loopback CI broker. Shared exchanges and external queues survive.
            foreach (var owned in new[] { queue + ".retry", queue + ".dead", queue })
                await publisher.QueueDeleteAsync(owned, false, false, cancellationToken: CancellationToken.None);
        }
    }

    private static NotificationDelivery Delivery(BasicGetResult value) => new(value.BasicProperties.MessageId, value.Body.ToArray(),
        NotificationDeliveryMetadata.GetRabbitAttempt(value.BasicProperties.Headers));
    private static RabbitNotificationSettlement Settlement(IChannel consumer, IChannel publisher, SemaphoreSlim gate,
        string queue, BasicGetResult value, CancellationToken ct) => new(consumer, publisher, gate, queue,
            new BasicDeliverEventArgs("test", value.DeliveryTag, value.Redelivered, value.Exchange, value.RoutingKey, value.BasicProperties, value.Body.ToArray(), ct));
    private static async Task<BasicGetResult> ReceiveAsync(IChannel channel, string queue, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        while (true)
        {
            var value = await channel.BasicGetAsync(queue, false, deadline.Token);
            if (value is not null) return value;
            await Task.Delay(100, deadline.Token);
        }
    }
    private static string Header(BasicGetResult value, string name) => value.BasicProperties.Headers![name] is byte[] bytes
        ? Encoding.UTF8.GetString(bytes) : (string)value.BasicProperties.Headers[name]!;
    private static async Task AssertOneSqlNotificationAsync(SqlServerNotificationsConcurrencyTests.Fixture fixture) =>
        await fixture.Factory.WithDatabaseAsync(async db =>
        {
            Assert.Single(await db.UserNotifications.ToListAsync());
            Assert.Single(await db.NotificationProcessingMarkers.ToListAsync());
        });
    private sealed class Hints : IInboxRealtimePublisher
    {
        public int Count { get; private set; }
        public Task PublishAsync(Guid notificationId, int recipientId, CancellationToken ct = default) { Count++; return Task.CompletedTask; }
    }
    private sealed class Faults { public int Calls; public int Remaining; }
    private sealed class ControlledProcessor(TaskContext db, TimeProvider clock, Faults faults) : INotificationMessageProcessor
    {
        public Task<NotificationProcessingResult> ProcessAsync(Guid messageId, string immutablePayload, CancellationToken ct = default)
        {
            faults.Calls++;
            if (faults.Remaining-- > 0) throw new IOException("Explicit synthetic processing failure.");
            return new NotificationMessageProcessor(db, clock, NullLogger<NotificationMessageProcessor>.Instance).ProcessAsync(messageId, immutablePayload, ct);
        }
    }
    private sealed class DropAcknowledgement(INotificationDeliverySettlement actual) : INotificationDeliverySettlement
    {
        public Task CompleteAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RetryAsync(int nextAttempt, CancellationToken ct) => actual.RetryAsync(nextAttempt, ct);
        public Task DeadLetterAsync(string safeReason, CancellationToken ct) => actual.DeadLetterAsync(safeReason, ct);
    }
}

public sealed class RabbitSqlFactAttribute : FactAttribute
{
    public RabbitSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TASKMANAGER_TEST_RABBITMQ") != "true"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TASKMANAGER_TEST_SQL_SERVER")))
            Skip = "Requires the explicit disposable loopback RabbitMQ and isolated SQL Server CI services.";
    }
}
