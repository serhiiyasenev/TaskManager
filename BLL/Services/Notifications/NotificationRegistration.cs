using Azure.Messaging.ServiceBus;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Services.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BLL.Services.Notifications;

public static class NotificationRegistration
{
    public static IServiceCollection AddNotificationStorage(this IServiceCollection services, bool includeHttpInbox = true)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        if (includeHttpInbox) services.AddScoped<INotificationInboxService, NotificationInboxService>();
        services.AddScoped<INotificationMessageProcessor, NotificationMessageProcessor>();
        services.AddScoped<IAiDraftRetentionService, AiDraftRetentionService>();
        services.AddScoped<INotificationRetentionService, NotificationRetentionService>();
        services.AddSingleton<ITaskNotificationDispatcher, TaskNotificationDispatcher>();
        return services;
    }

    public static IServiceCollection AddNotificationTransport(this IServiceCollection services, IConfiguration configuration)
    {
        var selected = configuration.GetSection(NotificationTransportOptions.SectionName).Get<NotificationTransportOptions>() ?? new();
        selected.Validate();
        services.AddOptions<NotificationTransportOptions>().Bind(configuration.GetSection(NotificationTransportOptions.SectionName))
            .Validate(options => options.Provider is "RabbitMQ" or "AzureServiceBus", "Unsupported notification transport.").ValidateOnStart();
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<ReminderOptions>(configuration.GetSection(ReminderOptions.SectionName));
        if (selected.Provider == "AzureServiceBus")
        {
            services.AddOptions<ServiceBusOptions>().Bind(configuration.GetSection(ServiceBusOptions.SectionName))
                .Validate(options => { try { options.Validate(); return true; } catch (InvalidOperationException) { return false; } },
                    "Service Bus namespace, identity or bounded settings are invalid.").ValidateOnStart();
            // Outbox producers keep one logical routing contract for both
            // adapters; Azure selection explicitly maps configured queue names.
            services.PostConfigure<RabbitMqOptions>(options =>
            {
                var bus = configuration.GetSection(ServiceBusOptions.SectionName).Get<ServiceBusOptions>() ?? new();
                options.QueueName = bus.QueueName;
                options.ReminderQueueName = bus.ReminderQueueName;
            });
            services.AddSingleton(provider => ServiceBusClientFactory.Create(provider.GetRequiredService<IOptions<ServiceBusOptions>>().Value));
            services.AddSingleton<IQueueService, AzureServiceBusQueueService>();
        }
        else services.AddSingleton<IQueueService, RabbitMqService>();
        return services;
    }
}
