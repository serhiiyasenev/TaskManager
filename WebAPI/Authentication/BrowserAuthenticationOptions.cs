namespace WebAPI.Authentication;

public sealed class BrowserAuthenticationOptions
{
    public const string SectionName = "BrowserAuth";
    public string PublicOrigin { get; set; } = "https://localhost";
    public string[] AdditionalAllowedOrigins { get; set; } = [];
    public int SessionMinutes { get; set; } = 60;
    public int AuthRequestsPerMinute { get; set; } = 30;
}

public sealed class AccountEmailOptions
{
    public const string SectionName = "Email";
    public string Provider { get; set; } = "Smtp";
    public string? AzureCommunicationEndpoint { get; set; }
    public string? ManagedIdentityClientId { get; set; }
    public int SendTimeoutSeconds { get; set; } = 30;
    public string SmtpHost { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 1025;
    public bool UseTls { get; set; }
    public string FromAddress { get; set; } = "noreply@taskmanager.local";
    public string? UserName { get; set; }
    public string? Password { get; set; }
}
