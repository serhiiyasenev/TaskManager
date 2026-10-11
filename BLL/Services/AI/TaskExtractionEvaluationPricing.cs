#nullable enable
namespace BLL.Services.AI;

public sealed record TaskExtractionCostEstimate(decimal Amount, bool UsesReportedUsage);
public sealed record TaskExtractionEvaluationPricing(decimal InputPricePerMillion, decimal OutputPricePerMillion,
    string Currency, decimal MaximumEstimatedCost)
{
    public void Validate() => throw new NotImplementedException();
    public decimal ReserveCost(long reservedTokens) => throw new NotImplementedException();
    public TaskExtractionCostEstimate Estimate(int? inputTokens, int? outputTokens, int reservedTokens)
        => throw new NotImplementedException();
}
