#nullable enable
namespace BLL.Configuration;

public sealed class TaskExtractionOptions
{
    public const string SectionName = "AI";
    public string Provider { get; set; } = "Disabled";
    public string? Endpoint { get; set; }
    public string? Deployment { get; set; }
    public string? ManagedIdentityClientId { get; set; }
    public string? ApiKey { get; set; }
    public int MaxInputCharacters { get; set; } = 20_000;
    public int MaxItems { get; set; } = 25;
    public int MaxOutputTokens { get; set; } = 4_000;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxInferenceAttempts { get; set; } = 1;
    public int DraftLifetimeHours { get; set; } = 24;
    public int ReceiptLifetimeDays { get; set; } = 7;
    public int UserDailyTokenBudget { get; set; } = 100_000;
    public int ProductDailyTokenBudget { get; set; } = 1_000_000;
    public int RequestsPerMinute { get; set; } = 6;
}
