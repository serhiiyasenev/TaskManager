#nullable enable
using BLL.Models.AI;
using BLL.Configuration;
using BLL.Interfaces;
using Microsoft.Extensions.Hosting;

namespace BLL.Services.AI;

public sealed record TaskExtractionChatRequest(string SystemMessage, string UserMessage, string JsonSchema, int MaxOutputTokens);

public interface ITaskExtractionChatTransport
{
    Task<TaskExtractionProviderResult> CompleteAsync(TaskExtractionChatRequest request, CancellationToken ct);
}

public sealed class AzureTaskExtractionProvider : ITaskExtractionProvider
{
    public AzureTaskExtractionProvider(TaskExtractionOptions options, IHostEnvironment environment,
        ITaskExtractionChatTransport transport) => throw new NotImplementedException();
    public bool IsAvailable => throw new NotImplementedException();
    public string ProviderName => throw new NotImplementedException();
    public string? UnavailableReasonCode => throw new NotImplementedException();
    public int CalculateReservationTokens(TaskExtractionProviderInput input) => throw new NotImplementedException();
    public Task<TaskExtractionProviderResult> ExtractAsync(TaskExtractionProviderInput input, CancellationToken ct = default)
        => throw new NotImplementedException();
}
