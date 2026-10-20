using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BLL.Interfaces;
using BLL.Models.Messaging;
using DAL.Context;
using DAL.Entities;
using DAL.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Integration;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Notifications;

public sealed class ExecutedTaskOutboxTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyCompletionCommitsStablePrivateIntentBeforeConfirmedSend(bool confirmed)
    {
        var dispatcher = new CaptureDispatcher(confirmed);
        using var factory = await DataIsolationFactory.CreateAsync(configureServices: services =>
        {
            services.RemoveAll<ITaskNotificationDispatcher>();
            services.AddSingleton<ITaskNotificationDispatcher>(dispatcher);
        });
        await factory.WithDatabaseAsync(async db =>
        {
            (await db.Tasks.FindAsync(101))!.State = TaskState.Done;
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateUserClient(101);
        using var response = await client.PostAsJsonAsync("/api/tasks/AddExecutedTask",
            new { taskId = 101, taskName = "spoofed title", notificationMessageId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(confirmed, json.RootElement.GetProperty("postedMessageToQueueResult").GetBoolean());
        Assert.False(json.RootElement.GetProperty("task").TryGetProperty("notificationMessageId", out _));
        await factory.WithDatabaseAsync(async db =>
        {
            var executed = await db.ExecutedTasks.SingleAsync();
            var message = await db.NotificationOutbox.SingleAsync();
            Assert.Equal(message.Id, executed.NotificationMessageId);
            Assert.Equal(message.Id, dispatcher.MessageId);
            Assert.Equal(NotificationOutboxKind.ExecutedTaskCompleted, message.Kind);
            Assert.Equal(NotificationOutboxStatus.Pending, message.Status);
            var envelope = JsonSerializer.Deserialize<TaskNotificationEnvelope>(message.Payload)!;
            Assert.Equal(NotificationEventType.ExecutedTaskCompleted, envelope.Type);
            Assert.Equal(message.Id.ToString("N"), envelope.CorrelationId);
            Assert.Equal("Team A task", envelope.ExecutedTask!.TaskName);
            Assert.Equal(101, envelope.ExecutedTask.PerformerId);
            Assert.Equal(101, envelope.ExecutedTask.ProjectId);
        });
    }

    [Fact]
    public async Task OutboxSaveFailureRollsBackExecutionAndNeverCallsBroker()
    {
        var failure = new FailOutbox();
        var dispatcher = new CaptureDispatcher(true);
        using var factory = await DataIsolationFactory.CreateAsync(interceptor: failure, configureServices: services =>
            services.AddSingleton<ITaskNotificationDispatcher>(dispatcher));
        await factory.WithDatabaseAsync(async db =>
        {
            (await db.Tasks.FindAsync(101))!.State = TaskState.Done;
            await db.SaveChangesAsync();
        });
        failure.Enabled = true;
        using var client = factory.CreateUserClient(101);
        using var response = await client.PostAsJsonAsync("/api/tasks/AddExecutedTask", new { taskId = 101, taskName = "Untrusted client title" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Null(dispatcher.MessageId);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Empty(await db.ExecutedTasks.ToListAsync());
            Assert.Empty(await db.NotificationOutbox.ToListAsync());
        });
    }

    private sealed class CaptureDispatcher(bool result) : ITaskNotificationDispatcher
    {
        public Guid? MessageId { get; private set; }
        public Task<bool> TryDispatchAsync(Guid messageId, CancellationToken ct = default)
        { MessageId = messageId; return Task.FromResult(result); }
    }
    private sealed class FailOutbox : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<NotificationOutboxMessage>()
                .Any(entry => entry.State == EntityState.Added)) throw new IOException("Synthetic outbox failure");
            return ValueTask.FromResult(result);
        }
    }
}
