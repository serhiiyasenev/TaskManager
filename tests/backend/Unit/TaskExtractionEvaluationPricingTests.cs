using BLL.Services.AI;
using Xunit;

namespace Tests.Unit;

public class TaskExtractionEvaluationPricingTests
{
    [Fact]
    public void ReportedUsage_UsesDistinctConfiguredInputAndOutputRates()
    {
        var pricing = new TaskExtractionEvaluationPricing(2m, 8m, "USD", 10m);
        var estimate = pricing.Estimate(1_000_000, 500_000, 2_000_000);
        Assert.Equal(6m, estimate.Amount);
        Assert.True(estimate.UsesReportedUsage);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(100, null)]
    [InlineData(null, 100)]
    public void UnknownUsage_KeepsFullReservationAtHigherConfiguredRate(int? input, int? output)
    {
        var estimate = new TaskExtractionEvaluationPricing(2m, 8m, "EUR", 10m).Estimate(input, output, 1000);
        Assert.Equal(0.008m, estimate.Amount);
        Assert.False(estimate.UsesReportedUsage);
    }

    [Fact]
    public void PreflightCost_IsConservativeAcrossUnknownInputOutputMix()
    {
        var pricing = new TaskExtractionEvaluationPricing(0.5m, 2m, "USD", 0.019m);
        Assert.Equal(0.02m, pricing.ReserveCost(10_000));
        Assert.True(pricing.ReserveCost(10_000) > pricing.MaximumEstimatedCost);
    }

    [Theory]
    [InlineData(-1, 1, "USD", 10)]
    [InlineData(1, -1, "USD", 10)]
    [InlineData(1, 1, "", 10)]
    [InlineData(1, 1, "not-a-currency", 10)]
    [InlineData(1, 1, "USD", 0)]
    public void InvalidOrMissingPricing_CannotAuthorizeLiveInference(int inputPrice, int outputPrice, string currency, int maximum)
        => Assert.Throws<InvalidOperationException>(() => new TaskExtractionEvaluationPricing(inputPrice, outputPrice, currency, maximum).Validate());
}
