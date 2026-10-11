using System.Text;
using BLL.Interfaces;
using BLL.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Unit;

public sealed class NotificationDeliveryCoordinatorTests
{
    private static readonly Guid MessageId = Guid.Parse("c4ea0f2b-9452-48b0-84f5-b3d315d44d02");

    [Fact]
    public async Task SqlCommitMustFinishBeforeAnyAcknowledgementOrHint()
    {
        using var h = new Harness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Processor.Process = async (_, _, ct) => { entered.SetResult(); await commit.Task.WaitAsync(ct); return Created(); };
        var work = h.Coordinator.ProcessAsync(Delivery(), h.Settlement);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Empty(h.Events);
        }
        finally { commit.TrySetResult(); }
        await work;
        Assert.Equal(1, h.Settlement.Completions);
        Assert.Equal(1, h.Hints.Calls);
    }

    [Fact]
    public async Task OriginalUtf8BodyAndStableIdReachScopedProcessorWithoutRewriting()
    {
        using var h = new Harness();
        var body = $"{{\"CorrelationId\":\"{MessageId:N}\",\"TaskName\":\"Перевірити 🚀\"}}";
        await h.Coordinator.ProcessAsync(new(MessageId.ToString("N"), Encoding.UTF8.GetBytes(body)), h.Settlement);
        Assert.Equal(MessageId, h.Processor.LastId);
        Assert.Equal(body, h.Processor.LastBody);
        Assert.Equal(1, h.Processor.DisposeCount);
        Assert.Equal(1, h.Settlement.Completions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DuplicateOrSuppressedOutcomeCompletesWithoutSecondHint(bool created, bool suppressed)
    {
        using var h = new Harness();
        h.Processor.Process = (_, _, _) => Task.FromResult(new NotificationProcessingResult(MessageId, 7, created, suppressed));
        await h.Coordinator.ProcessAsync(Delivery(), h.Settlement);
        Assert.Equal(1, h.Settlement.Completions);
        Assert.Equal(0, h.Hints.Calls);
        Assert.Empty(h.Settlement.RetryAttempts);
    }

    [Fact]
    public async Task HintFailureCannotUndoDurableCommitOrCauseBrokerRetry()
    {
        using var h = new Harness();
        h.Hints.Failure = new IOException("synthetic hint unavailable");
        await h.Coordinator.ProcessAsync(Delivery(), h.Settlement);
        Assert.Equal(1, h.Settlement.Completions);
        Assert.Equal(1, h.Hints.Calls);
        Assert.Empty(h.Settlement.RetryAttempts);
        Assert.Empty(h.Settlement.DeadReasons);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public async Task TransientSqlFailureRequestsNextBoundedAttempt(int attempt, int expectedNext)
    {
        using var h = new Harness();
        h.Processor.Process = (_, _, _) => throw new IOException("synthetic SQL unavailable");
        await h.Coordinator.ProcessAsync(Delivery(attempt), h.Settlement);
        Assert.Equal([expectedNext], h.Settlement.RetryAttempts);
        Assert.Equal(0, h.Settlement.Completions);
        Assert.Empty(h.Settlement.DeadReasons);
        Assert.Equal(0, h.Hints.Calls);
    }

    [Fact]
    public async Task ThirdTransientAttemptMovesToDeadLetterWithoutFourthAttempt()
    {
        using var h = new Harness();
        h.Processor.Process = (_, _, _) => throw new IOException("synthetic SQL unavailable");
        await h.Coordinator.ProcessAsync(Delivery(3), h.Settlement);
        Assert.Equal(["AttemptsExhausted"], h.Settlement.DeadReasons);
        Assert.Empty(h.Settlement.RetryAttempts);
        Assert.Equal(0, h.Settlement.Completions);
    }

    [Fact]
    public async Task PermanentInvalidEnvelopeUsesSafeDeadLetterReason()
    {
        using var h = new Harness();
        h.Processor.Process = (_, _, _) => throw new InvalidDataException("password=synthetic-sensitive-text");
        await h.Coordinator.ProcessAsync(Delivery(), h.Settlement);
        Assert.Equal(["InvalidNotification"], h.Settlement.DeadReasons);
        Assert.Empty(h.Settlement.RetryAttempts);
        Assert.Equal(0, h.Hints.Calls);
    }

    [Fact]
    public async Task FailedRetryPublishNeverCompletesOriginalDelivery()
    {
        using var h = new Harness();
        h.Processor.Process = (_, _, _) => throw new IOException("SQL unavailable");
        h.Settlement.RetryFailure = new IOException("confirm lost");
        await Assert.ThrowsAsync<IOException>(() => h.Coordinator.ProcessAsync(Delivery(), h.Settlement));
        Assert.Equal(0, h.Settlement.Completions);
        Assert.Empty(h.Settlement.DeadReasons);
    }

    [Fact]
    public async Task CompletionLossAfterSqlCommitDoesNotPublishRetryOrSecondHint()
    {
        using var h = new Harness();
        h.Settlement.CompletionFailure = new IOException("ack lost");
        await Assert.ThrowsAsync<IOException>(() => h.Coordinator.ProcessAsync(Delivery(), h.Settlement));
        Assert.Empty(h.Settlement.RetryAttempts);
        Assert.Empty(h.Settlement.DeadReasons);
        Assert.Equal(1, h.Processor.Calls);
    }

    [Fact]
    public async Task ShutdownBeforeProcessingLeavesDeliveryUnsettled()
    {
        using var h = new Harness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Coordinator.ProcessAsync(Delivery(), h.Settlement, cancellation.Token));
        Assert.Empty(h.Events);
        Assert.Equal(0, h.Processor.Calls);
    }

    [Fact]
    public async Task ShutdownDuringSqlCommitLeavesDeliveryUnsettled()
    {
        using var h = new Harness();
        using var cancellation = new CancellationTokenSource();
        h.Processor.Process = (_, _, ct) => { cancellation.Cancel(); return Task.FromCanceled<NotificationProcessingResult>(ct); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Coordinator.ProcessAsync(Delivery(), h.Settlement, cancellation.Token));
        Assert.Empty(h.Events);
    }

    [Theory]
    [InlineData("bad_utf8")]
    [InlineData("too_large")]
    [InlineData("missing_id")]
    [InlineData("empty_id")]
    [InlineData("different_id")]
    [InlineData("duplicate_id_property")]
    [InlineData("invalid_attempt")]
    public async Task TransportBoundaryRejectsUnusableBytesIdentityOrAttemptBeforeSql(string failure)
    {
        using var h = new Harness();
        var incoming = failure switch
        {
            "bad_utf8" => new NotificationDelivery(MessageId.ToString("N"), new byte[] { 0xC3, 0x28 }),
            "too_large" => new NotificationDelivery(MessageId.ToString("N"), new byte[65537]),
            "missing_id" => Delivery() with { MessageId = null },
            "empty_id" => Delivery() with { MessageId = Guid.Empty.ToString("N") },
            "different_id" => Delivery() with { MessageId = Guid.NewGuid().ToString("N") },
            "duplicate_id_property" => new NotificationDelivery(MessageId.ToString("N"), Encoding.UTF8.GetBytes($"{{\"CorrelationId\":\"{MessageId:N}\",\"correlationId\":\"{MessageId:N}\"}}")),
            _ => Delivery(0)
        };
        await h.Coordinator.ProcessAsync(incoming, h.Settlement);
        Assert.Equal(["InvalidNotification"], h.Settlement.DeadReasons);
        Assert.Equal(0, h.Processor.Calls);
        Assert.Equal(0, h.Settlement.Completions);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void RabbitHeaderCountsBusinessAttemptsIncludingInitialDelivery(int retryCount, int attempt) =>
        Assert.Equal(attempt, NotificationDeliveryMetadata.GetRabbitAttempt(new Dictionary<string, object?>
        { [NotificationDeliveryMetadata.RetryCountHeader] = retryCount, ["x-delivery-count"] = 9999L }));

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(1000000)]
    public void InvalidRabbitRetryCountCannotResetTheAttemptLimit(int count) =>
        Assert.Throws<InvalidDataException>(() => NotificationDeliveryMetadata.GetRabbitAttempt(new Dictionary<string, object?>
        { [NotificationDeliveryMetadata.RetryCountHeader] = count }));

    [Fact]
    public void NonNumericRabbitRetryHeaderIsRejected() => Assert.Throws<InvalidDataException>(() =>
        NotificationDeliveryMetadata.GetRabbitAttempt(new Dictionary<string, object?> { [NotificationDeliveryMetadata.RetryCountHeader] = "1" }));

    private static NotificationDelivery Delivery(int attempt = 1) => new(MessageId.ToString("N"),
        Encoding.UTF8.GetBytes($"{{\"CorrelationId\":\"{MessageId:N}\"}}"), attempt);
    private static NotificationProcessingResult Created() => new(MessageId, 7, true, false);

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;
        public List<string> Events { get; } = [];
        public Processor Processor { get; }
        public Hints Hints { get; }
        public Settlement Settlement { get; }
        public NotificationDeliveryCoordinator Coordinator { get; }
        public Harness()
        {
            Processor = new(); Hints = new(Events); Settlement = new(Events);
            _provider = new ServiceCollection().AddScoped<INotificationMessageProcessor>(_ => Processor).BuildServiceProvider();
            Coordinator = new(_provider.GetRequiredService<IServiceScopeFactory>(), Hints, NullLogger<NotificationDeliveryCoordinator>.Instance);
        }
        public void Dispose() => _provider.Dispose();
    }
    private sealed class Processor : INotificationMessageProcessor, IDisposable
    {
        public Func<Guid, string, CancellationToken, Task<NotificationProcessingResult>> Process { get; set; } = (_, _, _) => Task.FromResult(Created());
        public Guid LastId { get; private set; }
        public string? LastBody { get; private set; }
        public int Calls { get; private set; }
        public int DisposeCount { get; private set; }
        public Task<NotificationProcessingResult> ProcessAsync(Guid id, string body, CancellationToken ct = default)
        { Calls++; LastId = id; LastBody = body; return Process(id, body, ct); }
        public void Dispose() => DisposeCount++;
    }
    private sealed class Hints(List<string> events) : IInboxRealtimePublisher
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public Task PublishAsync(Guid id, int recipientId, CancellationToken ct = default)
        { Assert.Equal(MessageId, id); Assert.Equal(7, recipientId); Calls++; events.Add("hint"); return Failure is null ? Task.CompletedTask : Task.FromException(Failure); }
    }
    private sealed class Settlement(List<string> events) : INotificationDeliverySettlement
    {
        public int Completions { get; private set; }
        public List<int> RetryAttempts { get; } = [];
        public List<string> DeadReasons { get; } = [];
        public Exception? RetryFailure { get; set; }
        public Exception? CompletionFailure { get; set; }
        public Task CompleteAsync(CancellationToken ct)
        { Completions++; events.Add("complete"); return CompletionFailure is null ? Task.CompletedTask : Task.FromException(CompletionFailure); }
        public Task RetryAsync(int attempt, CancellationToken ct)
        { RetryAttempts.Add(attempt); events.Add("retry"); return RetryFailure is null ? Task.CompletedTask : Task.FromException(RetryFailure); }
        public Task DeadLetterAsync(string reason, CancellationToken ct)
        { DeadReasons.Add(reason); events.Add("dead"); return Task.CompletedTask; }
    }
}
