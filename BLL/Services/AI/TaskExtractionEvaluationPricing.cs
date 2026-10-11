#nullable enable
using System.Text.RegularExpressions;
namespace BLL.Services.AI;

public sealed record TaskExtractionCostEstimate(decimal Amount, bool UsesReportedUsage);
public sealed record TaskExtractionEvaluationPricing(decimal InputPricePerMillion, decimal OutputPricePerMillion,
    string Currency, decimal MaximumEstimatedCost)
{
    public void Validate()
    {
        if (InputPricePerMillion is < 0 or > 1_000_000 || OutputPricePerMillion is < 0 or > 1_000_000
            || MaximumEstimatedCost is <= 0 or > 1_000_000 || string.IsNullOrWhiteSpace(Currency)
            || !Regex.IsMatch(Currency, "^[A-Z]{3}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidOperationException("Evaluation pricing requires explicit valid rates, currency, and estimated cost cap.");
    }
    public decimal ReserveCost(long reservedTokens)
    {
        Validate();
        if (reservedTokens < 0) throw new InvalidOperationException("Reserved token count must be nonnegative.");
        return reservedTokens * Math.Max(InputPricePerMillion, OutputPricePerMillion) / 1_000_000m;
    }
    public TaskExtractionCostEstimate Estimate(int? inputTokens, int? outputTokens, int reservedTokens)
    {
        Validate();
        if (inputTokens is > 0 && outputTokens is >= 0)
            return new((inputTokens.Value * InputPricePerMillion + outputTokens.Value * OutputPricePerMillion) / 1_000_000m, true);
        return new(ReserveCost(reservedTokens), false);
    }
}
