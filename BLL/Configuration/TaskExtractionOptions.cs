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
    public int MaxAllowedAssignees { get; set; } = 200;
    public int MaxPromptUtf8Bytes { get; set; } = 128_000;

    public void Validate()
    {
        if (Provider is not ("Disabled" or "Azure" or "Fake")
            || MaxInputCharacters is < 1 or > 20_000 || MaxItems is < 1 or > 25
            || MaxOutputTokens is < 256 or > 8_000 || TimeoutSeconds is < 1 or > 120
            || MaxInferenceAttempts is < 1 or > 2 || DraftLifetimeHours is < 1 or > 168
            || ReceiptLifetimeDays is < 1 or > 30 || UserDailyTokenBudget < 1
            || ProductDailyTokenBudget < 1 || RequestsPerMinute is < 1 or > 120
            || MaxAllowedAssignees is < 1 or > 1000 || MaxPromptUtf8Bytes is < 4096 or > 1_000_000)
            throw new InvalidOperationException("AI configuration contains an unsupported provider or limit.");
        if (!string.IsNullOrEmpty(ManagedIdentityClientId) && !Guid.TryParse(ManagedIdentityClientId, out _))
            throw new InvalidOperationException("AI managed identity client ID must be a GUID.");
        if (!string.IsNullOrWhiteSpace(Endpoint)
            && (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/" || !uri.IsDefaultPort))
            throw new InvalidOperationException("AI endpoint must be an HTTPS resource root URL.");
        if (!string.IsNullOrEmpty(Deployment) && (Deployment.Length > 128
            || Deployment.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.')))
            throw new InvalidOperationException("AI deployment name is invalid.");
    }
}
