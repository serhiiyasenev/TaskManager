using BLL.Interfaces;
using BLL.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Unit;

public sealed class AiDraftCleanupWorkerTests
{
    [Fact]
    public async Task CleanupUsesRetentionOnlyScopedServiceAndDisposesIt()
    {
        var retention = new Retention();
        using var provider = new ServiceCollection().AddScoped<IAiDraftRetentionService>(_ => retention).BuildServiceProvider();
        var worker = new AiDraftCleanupWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiDraftCleanupWorker>.Instance);
        Assert.Equal(3, await worker.RunOnceAsync());
        Assert.Equal(1, retention.Calls);
        Assert.True(retention.Disposed);
    }

    [Fact]
    public async Task CanceledCleanupDoesNotResolveOrCallRetention()
    {
        var retention = new Retention();
        using var provider = new ServiceCollection().AddScoped<IAiDraftRetentionService>(_ => retention).BuildServiceProvider();
        var worker = new AiDraftCleanupWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiDraftCleanupWorker>.Instance);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunOnceAsync(cancellation.Token));
        Assert.Equal(0, retention.Calls);
    }

    private sealed class Retention : IAiDraftRetentionService, IDisposable
    {
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }
        public Task<int> CleanupExpiredAsync(CancellationToken ct = default) { Calls++; return Task.FromResult(3); }
        public void Dispose() => Disposed = true;
    }
}
