#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BLL.Interfaces;

namespace BLL.Services.Notifications;

public sealed class AiDraftCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<AiDraftCleanupWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("AI retention batch failed; cleanup will retry on the next poll."); }
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAiDraftRetentionService>().CleanupExpiredAsync(ct);
    }
}
