using System.Text;
using BLL.Configuration;
using BLL.Models.AI;
using BLL.Services.AI;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Tests.Unit;

public class TaskExtractionProviderTests
{
    [Fact]
    public void ProviderMetadata_UsesPublicLowercaseNames()
        => Assert.Equal("azure", Provider(AzureOptions(), new()).ProviderName);

    [Fact]
    public async Task DisabledProvider_IsUnavailableAndMakesNoInference()
    {
        var transport = new RecordingTransport();
        var provider = Provider(new(), transport);
        Assert.False(provider.IsAvailable);
        var error = await Assert.ThrowsAsync<TaskExtractionProviderException>(() => provider.ExtractAsync(Input()));
        Assert.Equal("Ai.Unavailable", error.SafeCode);
        Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void FakeOutsideTesting_FailsConfiguration(string environment)
        => Assert.Throws<InvalidOperationException>(() => Provider(new() { Provider = "Fake" }, new(), environment));

    [Fact]
    public async Task UnconfiguredAzure_IsUnavailableWithoutProductionFallback()
    {
        var transport = new RecordingTransport();
        var provider = Provider(new() { Provider = "Azure" }, transport, "Production");
        Assert.False(provider.IsAvailable);
        await Assert.ThrowsAsync<TaskExtractionProviderException>(() => provider.ExtractAsync(Input()));
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task RequestContainsOnlyAllowedNamesAndFixedReference_NoToolsOrHistory()
    {
        var transport = new RecordingTransport();
        await Provider(AzureOptions(), transport).ExtractAsync(Input());
        var request = Assert.IsType<TaskExtractionChatRequest>(transport.Request);
        Assert.Contains("untrusted", request.SystemMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026-10-12", request.UserMessage);
        Assert.Contains("Europe/Kyiv", request.UserMessage);
        Assert.Contains("Alice Brown", request.UserMessage);
        Assert.DoesNotContain("ApiKey", request.UserMessage);
        Assert.Contains("additionalProperties", request.JsonSchema);
        Assert.Equal(4000, request.MaxOutputTokens);
    }

    [Fact]
    public async Task ReservationIncludesFullUtf8PromptSchemaOutputAndAllAttempts()
    {
        var transport = new RecordingTransport();
        var options = AzureOptions(); options.MaxInferenceAttempts = 2;
        var provider = Provider(options, transport);
        var input = Input() with { NotesText = new string('ї', 1000) };
        var reserved = provider.CalculateReservationTokens(input);
        await provider.ExtractAsync(input);
        var request = transport.Request!;
        var bytes = Encoding.UTF8.GetByteCount(request.SystemMessage + request.UserMessage + request.JsonSchema);
        Assert.True(reserved >= 2 * (bytes + options.MaxOutputTokens + 1024));
    }

    [Fact]
    public async Task InputLimit_PreventsTransportCall()
    {
        var options = AzureOptions(); options.MaxInputCharacters = 100;
        var transport = new RecordingTransport();
        var error = await Assert.ThrowsAsync<TaskExtractionProviderException>(() => Provider(options, transport).ExtractAsync(Input() with { NotesText = new string('x', 101) }));
        Assert.Equal("Ai.InputLimit", error.SafeCode);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Cancellation_PropagatesWithoutRetry()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var transport = new RecordingTransport();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(AzureOptions(), transport).ExtractAsync(Input(), cancellation.Token));
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Timeout_CancelsTransportAndReturnsSafeFailure()
    {
        var options = AzureOptions(); options.TimeoutSeconds = 1;
        var transport = new RecordingTransport { Handler = async ct => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new("{}"); } };
        var error = await Assert.ThrowsAsync<TaskExtractionProviderException>(() => Provider(options, transport).ExtractAsync(Input()));
        Assert.Equal("Ai.Timeout", error.SafeCode);
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData("Ai.Authentication")]
    [InlineData("Ai.InvalidRequest")]
    [InlineData("Ai.RateLimited")]
    public async Task PermanentOrRateLimitFailure_IsNotRetried(string code)
    {
        var options = AzureOptions(); options.MaxInferenceAttempts = 2;
        var transport = new RecordingTransport { Handler = _ => throw new TaskExtractionProviderException(code, 30) };
        var error = await Assert.ThrowsAsync<TaskExtractionProviderException>(() => Provider(options, transport).ExtractAsync(Input()));
        Assert.Equal(code, error.SafeCode);
        Assert.Equal(1, transport.Calls);
        Assert.DoesNotContain(Input().NotesText, error.Message);
    }

    [Fact]
    public async Task TransientFailure_HasBoundedExplicitRetry()
    {
        var options = AzureOptions(); options.MaxInferenceAttempts = 2;
        var transport = new RecordingTransport { Handler = _ => throw new TaskExtractionProviderException("Ai.Transient") };
        await Assert.ThrowsAsync<TaskExtractionProviderException>(() => Provider(options, transport).ExtractAsync(Input()));
        Assert.Equal(2, transport.Calls);
    }

    [Fact]
    public async Task UsageAndRefusal_AreReturnedWithoutFabricatedItems()
    {
        var transport = new RecordingTransport { Handler = _ => Task.FromResult(new TaskExtractionProviderResult("", true, false, 100, 20, "configured-model")) };
        var result = await Provider(AzureOptions(), transport).ExtractAsync(Input());
        Assert.True(result.Refused);
        Assert.Equal("", result.RawJson);
        Assert.Equal(100, result.InputTokens);
        Assert.Equal(20, result.OutputTokens);
    }

    private static TaskExtractionProviderInput Input() => new("Alice will check API tomorrow.", 7, new(2026, 10, 12), "Europe/Kyiv", "en", [new(12, "Alice Brown")]);
    private static TaskExtractionOptions AzureOptions() => new() { Provider = "Azure", Endpoint = "https://taskmanager-test.openai.azure.com/", Deployment = "configured-deployment" };
    private static AzureTaskExtractionProvider Provider(TaskExtractionOptions options, RecordingTransport transport, string environment = "Testing") => new(options, new EnvironmentStub(environment), transport);
    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class RecordingTransport : ITaskExtractionChatTransport
    {
        public int Calls { get; private set; }
        public TaskExtractionChatRequest? Request { get; private set; }
        public Func<CancellationToken, Task<TaskExtractionProviderResult>> Handler { get; init; } = _ => Task.FromResult(new TaskExtractionProviderResult("{\"items\":[]}", InputTokens: 100, OutputTokens: 20));
        public Task<TaskExtractionProviderResult> CompleteAsync(TaskExtractionChatRequest request, CancellationToken ct)
        {
            Calls++; Request = request; return Handler(ct);
        }
    }
}
