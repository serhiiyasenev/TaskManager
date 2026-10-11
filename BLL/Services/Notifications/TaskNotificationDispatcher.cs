#nullable enable
using BLL.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BLL.Services.Notifications;

public sealed class TaskNotificationDispatcher(
    IServiceScopeFactory scopeFactory, ILogger<TaskNotificationDispatcher> logger,
    TimeProvider? timeProvider = null) : ITaskNotificationDispatcher
{
    public Task<bool> TryDispatchAsync(Guid messageId, CancellationToken ct = default) => throw new NotImplementedException();
}
