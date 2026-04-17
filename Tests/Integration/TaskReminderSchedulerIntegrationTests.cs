using System.Text.Json;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Models.Messaging;
using BLL.Services;
using DAL.Context;
using DAL.Enum;
using DAL.Repositories.Implementation;
using DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebAPI.Services;
using Xunit;
using TaskEntity = DAL.Entities.Task;

namespace Tests.Integration;

public class TaskReminderSchedulerIntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private class InMemoryQueueService : IQueueService
    {
        public List<(string message, string? queue)> Messages { get; } = new();

        public Task<bool> PostValue(string message, string? queueName = null, CancellationToken ct = default)
        {
            Messages.Add((message, queueName));
            return Task.FromResult(true);
        }
    }

    private TaskReminderScheduler CreateScheduler(TaskContext context, InMemoryQueueService queue, ReminderOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<DbContext>(context);
        services.AddSingleton<TaskContext>(context);
        services.AddScoped(typeof(IRepository<>), typeof(EfCoreRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IQueueService>(queue);
        services.AddLogging();
        services.Configure<RabbitMqOptions>(opt =>
        {
            opt.ReminderQueueName = "TaskReminders";
        });
        services.Configure<ReminderOptions>(_ => { });
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return new TaskReminderScheduler(
            scopeFactory,
            Options.Create(options ?? new ReminderOptions()),
            Options.Create(new RabbitMqOptions { ReminderQueueName = "TaskReminders" }),
            NullLogger<TaskReminderScheduler>.Instance);
    }

    [Fact]
    public async Task RunOnceAsync_PublishesReminderEnvelope_WhenDueSoon()
    {
        fixture.ResetDatabase();
        var context = fixture.Context;
        var queue = new InMemoryQueueService();

        var task = new TaskEntity
        {
            Name = "Reminder Task",
            Description = "Due soon",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.ToDo,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            DueDate = DateTime.UtcNow.AddMinutes(5),
            ReminderEnabled = true,
            ReminderOffsetMinutes = 15
        };
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        var scheduler = CreateScheduler(context, queue, new ReminderOptions { ReminderQueueName = "TaskReminders" });

        await scheduler.RunOnceAsync();

        Assert.Single(queue.Messages);
        var payload = queue.Messages.Single().message;
        var envelope = JsonSerializer.Deserialize<TaskNotificationEnvelope>(payload);
        Assert.NotNull(envelope);
        Assert.Equal(NotificationEventType.TaskReminder, envelope!.Type);
        Assert.NotNull(envelope.Reminder);
        Assert.Equal(task.Id, envelope.Reminder!.TaskId);
        Assert.Equal("TaskReminders", queue.Messages.Single().queue);
    }

    [Fact]
    public async Task RunOnceAsync_PublishesOverdueEnvelope_WhenPastDue()
    {
        fixture.ResetDatabase();
        var context = fixture.Context;
        var queue = new InMemoryQueueService();

        var task = new TaskEntity
        {
            Name = "Overdue Task",
            Description = "Past due",
            ProjectId = 1,
            PerformerId = 1,
            State = TaskState.InProgress,
            CreatedAt = DateTime.UtcNow.AddHours(-3),
            DueDate = DateTime.UtcNow.AddMinutes(-10),
            EscalationEnabled = true,
            EscalationDelayMinutes = 5
        };
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        var scheduler = CreateScheduler(context, queue, new ReminderOptions { ReminderQueueName = "TaskReminders" });

        await scheduler.RunOnceAsync();

        var envelope = JsonSerializer.Deserialize<TaskNotificationEnvelope>(queue.Messages.Single().message);
        Assert.NotNull(envelope);
        Assert.Equal(NotificationEventType.TaskOverdueEscalation, envelope!.Type);
        Assert.Equal(task.Id, envelope.Reminder!.TaskId);
    }
}
