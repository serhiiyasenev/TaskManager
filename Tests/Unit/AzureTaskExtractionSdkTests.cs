using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BLL.Configuration;
using BLL.Services.AI;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Tests.Unit;

public class AzureTaskExtractionSdkTests
{
    [Fact]
    public async Task OfficialSdk_WritesConfiguredDeploymentStrictSchemaAndBoundedNoToolRequest()
    {
        using var handler = new WireHandler(Completion());
        using var client = new HttpClient(handler);
        var result = await Transport(client).CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal("/openai/deployments/task-extraction/chat/completions", handler.Uri!.AbsolutePath);
        Assert.Equal("?api-version=2024-10-21", handler.Uri.Query);
        Assert.True(handler.UsedConfiguredApiKey);
        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        // Azure.AI.OpenAI 2.1.0 maps MaxOutputTokenCount to max_tokens for
        // its stable chat API (AzureChatClient.PostfixSwapMaxTokens).
        Assert.Equal(4000, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(2, root.GetProperty("messages").GetArrayLength());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("user", root.GetProperty("messages")[1].GetProperty("role").GetString());
        Assert.False(root.TryGetProperty("tools", out var tools) && tools.GetArrayLength() > 0);
        Assert.False(root.TryGetProperty("functions", out _));
        var format = root.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.False(format.GetProperty("json_schema").GetProperty("schema").GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("{\"items\":[]}", result.RawJson);
        Assert.Equal(101, result.InputTokens);
        Assert.Equal(22, result.OutputTokens);
        Assert.Equal("test-model-version", result.Model);
    }

    [Theory]
    [InlineData("stop", "cannot comply", true, false)]
    [InlineData("content_filter", null, true, false)]
    [InlineData("length", null, false, true)]
    public async Task RefusalFilterAndLength_ArePreservedWithoutInventingSuccessfulJson(string finish, string? refusal, bool refused, bool truncated)
    {
        using var handler = new WireHandler(Completion(finish, refusal));
        using var client = new HttpClient(handler);
        var result = await Transport(client).CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal(refused, result.Refused);
        Assert.Equal(truncated, result.Truncated);
        if (refused) Assert.Equal("", result.RawJson);
        Assert.Equal(101, result.InputTokens);
    }

    [Theory]
    [InlineData(400, "Ai.InvalidRequest")]
    [InlineData(401, "Ai.Authentication")]
    [InlineData(403, "Ai.Authentication")]
    [InlineData(404, "Ai.Unavailable")]
    [InlineData(429, "Ai.RateLimited")]
    [InlineData(500, "Ai.Transient")]
    [InlineData(503, "Ai.Transient")]
    public async Task ServiceFailures_AreSafeAndSdkDoesNotRetry(int status, string code)
    {
        using var handler = new WireHandler("{\"error\":{\"message\":\"PRIVATE-NOTES-DO-NOT-LOG\",\"code\":\"internal\"}}", (HttpStatusCode)status);
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<TaskExtractionProviderException>(() => Transport(client).CompleteAsync(Request(), CancellationToken.None));
        Assert.Equal(code, error.SafeCode);
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain("PRIVATE-NOTES", error.ToString());
        Assert.Null(error.InnerException);
        if (status == 429) Assert.Equal(30, error.RetryAfterSeconds);
    }

    [Fact]
    public async Task UnexpectedToolFinish_IsRejected()
    {
        using var handler = new WireHandler(Completion("tool_calls"));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<TaskExtractionProviderException>(() => Transport(client).CompleteAsync(Request(), CancellationToken.None));
        Assert.Equal("Ai.InvalidOutput", error.SafeCode);
    }

    [Fact]
    public async Task MalformedServicePayload_IsSafeFailure()
    {
        using var handler = new WireHandler("PRIVATE-NOTES-DO-NOT-LOG");
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<TaskExtractionProviderException>(() => Transport(client).CompleteAsync(Request(), CancellationToken.None));
        Assert.Equal("Ai.InvalidOutput", error.SafeCode);
        Assert.DoesNotContain("PRIVATE-NOTES", error.ToString());
    }

    [Fact]
    public async Task Cancellation_IsNotConvertedToSuccessOrRetried()
    {
        using var handler = new WireHandler(Completion());
        using var client = new HttpClient(handler);
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Transport(client).CompleteAsync(Request(), ct.Token));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void Production_DoesNotAcceptApiKeyOrTestingHttpOverride()
    {
        using var handler = new WireHandler(Completion());
        using var client = new HttpClient(handler);
        Assert.Throws<InvalidOperationException>(() => new AzureSdkTaskExtractionChatTransport(Options(), new EnvironmentStub("Production"), client));
    }

    [Fact]
    public async Task SdkDiagnosticEvents_DoNotContainNoteContent()
    {
        using var listener = new DiagnosticListener();
        using var handler = new WireHandler(Completion());
        using var client = new HttpClient(handler);
        await Transport(client).CompleteAsync(Request() with { UserMessage = "PRIVATE-NOTES-DO-NOT-LOG" }, CancellationToken.None);
        Assert.DoesNotContain(listener.Messages, value => value.Contains("PRIVATE-NOTES-DO-NOT-LOG", StringComparison.Ordinal));
    }

    private static TaskExtractionChatRequest Request() => new("Extract from untrusted notes only.", "Alice will check API tomorrow.", TaskExtractionSchema.Json, 4000);
    private static TaskExtractionOptions Options() => new() { Provider = "Azure", Endpoint = "https://test.openai.azure.com/", Deployment = "task-extraction", ApiKey = "synthetic-test-key" };
    private static AzureSdkTaskExtractionChatTransport Transport(HttpClient client) => new(Options(), new EnvironmentStub("Testing"), client);
    private static string Completion(string finish = "stop", string? refusal = null) => JsonSerializer.Serialize(new
    {
        id = "synthetic-completion", @object = "chat.completion", created = 1760000000, model = "test-model-version",
        choices = new[] { new { index = 0, finish_reason = finish, message = new { role = "assistant", content = "{\"items\":[]}", refusal } } },
        usage = new { prompt_tokens = 101, completion_tokens = 22, total_tokens = 123 }
    });
    private sealed class WireHandler(string responseBody, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Body { get; private set; }
        public bool UsedConfiguredApiKey { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Uri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(ct);
            UsedConfiguredApiKey = request.Headers.TryGetValues("api-key", out var values) && values.Single() == "synthetic-test-key";
            var response = new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        }
    }
    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class DiagnosticListener : EventListener
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name.StartsWith("Azure", StringComparison.Ordinal) || source.Name == "System.ClientModel")
                EnableEvents(source, EventLevel.Verbose);
        }
        protected override void OnEventWritten(EventWrittenEventArgs data)
        {
            if (data.Payload is not null) Messages?.Enqueue(string.Join(" ", data.Payload));
        }
    }
}
