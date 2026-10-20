#nullable enable
using BLL.Models.AI;

namespace BLL.Interfaces;

public interface ITaskExtractionProvider
{
    bool IsAvailable { get; }
    string ProviderName { get; }
    string? UnavailableReasonCode { get; }
    int CalculateReservationTokens(TaskExtractionProviderInput input);
    Task<TaskExtractionProviderResult> ExtractAsync(TaskExtractionProviderInput input, CancellationToken ct = default);
}
