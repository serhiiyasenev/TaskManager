#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BLL.Services.Notifications;

public sealed class AiDraftCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<AiDraftCleanupWorker> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => throw new NotImplementedException();
    public Task<int> RunOnceAsync(CancellationToken ct = default) => throw new NotImplementedException();
}
