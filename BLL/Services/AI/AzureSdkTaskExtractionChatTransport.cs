#nullable enable
using BLL.Configuration;
using BLL.Models.AI;
using Microsoft.Extensions.Hosting;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using System.Text.Json;

namespace BLL.Services.AI;

public sealed class AzureSdkTaskExtractionChatTransport : ITaskExtractionChatTransport
{
    private readonly ChatClient _client;
    public AzureSdkTaskExtractionChatTransport(TaskExtractionOptions options, IHostEnvironment environment,
        HttpClient? transportClient = null)
    {
        options.Validate();
        if (transportClient is not null && environment.EnvironmentName != "Testing")
            throw new InvalidOperationException("A custom AI HTTP transport is permitted only in Testing.");
        var development = environment.IsDevelopment() || environment.EnvironmentName == "Testing";
        if (!development && !string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("Deployed AI authentication requires managed identity.");
        if (string.IsNullOrWhiteSpace(options.Endpoint) || string.IsNullOrWhiteSpace(options.Deployment))
            throw new TaskExtractionProviderException("Ai.Unavailable");
        var clientOptions = new AzureOpenAIClientOptions
        {
            RetryPolicy = new ClientRetryPolicy(0), NetworkTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            EnableDistributedTracing = false,
            ClientLoggingOptions = new ClientLoggingOptions
            { EnableLogging = false, EnableMessageLogging = false, EnableMessageContentLogging = false }
        };
        if (transportClient is not null) clientOptions.Transport = new HttpClientPipelineTransport(transportClient);
        AzureOpenAIClient azure;
        if (development && !string.IsNullOrWhiteSpace(options.ApiKey))
            azure = new(new Uri(options.Endpoint), new ApiKeyCredential(options.ApiKey), clientOptions);
        else
            azure = new(new Uri(options.Endpoint), CreateCredential(options, development), clientOptions);
        _client = azure.GetChatClient(options.Deployment);
    }

    public async Task<TaskExtractionProviderResult> CompleteAsync(TaskExtractionChatRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = request.MaxOutputTokens,
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat("task_drafts", BinaryData.FromString(request.JsonSchema), jsonSchemaIsStrict: true)
            };
            ChatCompletion completion = (await _client.CompleteChatAsync(
                [new SystemChatMessage(request.SystemMessage), new UserChatMessage(request.UserMessage)], options, ct)).Value;
            if (completion.FinishReason is not (ChatFinishReason.Stop or ChatFinishReason.Length or ChatFinishReason.ContentFilter)
                || completion.ToolCalls.Count > 0)
                throw new TaskExtractionProviderException("Ai.InvalidOutput");
            var refused = !string.IsNullOrEmpty(completion.Refusal) || completion.FinishReason == ChatFinishReason.ContentFilter;
            var truncated = completion.FinishReason == ChatFinishReason.Length;
            var content = refused ? "" : string.Concat(completion.Content.Select(part => part.Text));
            return new(content, refused, truncated, completion.Usage?.InputTokenCount, completion.Usage?.OutputTokenCount, completion.Model);
        }
        catch (OperationCanceledException) { throw; }
        catch (TaskExtractionProviderException) { throw; }
        catch (AuthenticationFailedException) { throw new TaskExtractionProviderException("Ai.Authentication"); }
        catch (ClientResultException error)
        {
            var code = error.Status switch
            {
                401 or 403 => "Ai.Authentication", 404 => "Ai.Unavailable", 429 => "Ai.RateLimited",
                0 or >= 500 => "Ai.Transient", _ => "Ai.InvalidRequest"
            };
            throw new TaskExtractionProviderException(code, error.Status == 429 ? RetryAfter(error) : null);
        }
        catch (HttpRequestException) { throw new TaskExtractionProviderException("Ai.Transient"); }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException)
        { throw new TaskExtractionProviderException("Ai.InvalidOutput"); }
    }

    private static int? RetryAfter(ClientResultException error)
    {
        var response = error.GetRawResponse();
        if (response is null) return null;
        if (response.Headers.TryGetValue("Retry-After", out var value))
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return Math.Clamp(seconds, 1, 120);
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var due))
                return (int)Math.Clamp(Math.Ceiling((due - DateTimeOffset.UtcNow).TotalSeconds), 1, 120);
        }
        return null;
    }

    private static TokenCredential CreateCredential(TaskExtractionOptions options, bool development)
    {
        if (development)
        {
            var localOptions = new DefaultAzureCredentialOptions { ManagedIdentityClientId = options.ManagedIdentityClientId };
            localOptions.Diagnostics.IsLoggingEnabled = false;
            localOptions.Diagnostics.IsLoggingContentEnabled = false;
            localOptions.Diagnostics.IsAccountIdentifierLoggingEnabled = false;
            return new DefaultAzureCredential(localOptions);
        }
        var identity = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId);
        var managedOptions = new ManagedIdentityCredentialOptions(identity);
        managedOptions.Diagnostics.IsLoggingEnabled = false;
        managedOptions.Diagnostics.IsLoggingContentEnabled = false;
        managedOptions.Diagnostics.IsAccountIdentifierLoggingEnabled = false;
        return new ManagedIdentityCredential(managedOptions);
    }
}
