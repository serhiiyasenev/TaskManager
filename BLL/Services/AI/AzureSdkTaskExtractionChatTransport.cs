#nullable enable
using BLL.Configuration;
using BLL.Models.AI;
using Microsoft.Extensions.Hosting;

namespace BLL.Services.AI;

public sealed class AzureSdkTaskExtractionChatTransport : ITaskExtractionChatTransport
{
    public AzureSdkTaskExtractionChatTransport(TaskExtractionOptions options, IHostEnvironment environment,
        HttpClient? transportClient = null) => throw new NotImplementedException();
    public Task<TaskExtractionProviderResult> CompleteAsync(TaskExtractionChatRequest request, CancellationToken ct)
        => throw new NotImplementedException();
}
