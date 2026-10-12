using Azure.Identity;
using Microsoft.Azure.SignalR;
using System.Security.Claims;

namespace Notifier.Notifications;

public static class SignalRRegistration
{
    public static IServiceCollection AddPrivateNotificationSignalR(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Realtime:Provider"] ?? "Local";
        var signalR = services.AddSignalR(options => options.EnableDetailedErrors = false);
        if (provider == "Local") return services;
        if (provider != "AzureSignalR") throw new InvalidOperationException("Realtime:Provider must be Local or AzureSignalR.");
        if (!Uri.TryCreate(configuration["Realtime:Endpoint"], UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != "https" || endpoint.Port != 443 || endpoint.UserInfo.Length != 0
            || endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
            || !endpoint.Host.EndsWith(".service.signalr.net", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Realtime:Endpoint must be an Azure SignalR HTTPS resource endpoint.");
        var clientId = configuration["Realtime:ManagedIdentityClientId"];
        if (!string.IsNullOrWhiteSpace(clientId) && !Guid.TryParse(clientId, out _))
            throw new InvalidOperationException("Realtime:ManagedIdentityClientId must be a managed identity client GUID.");
        var identity = string.IsNullOrWhiteSpace(clientId) ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(clientId);
        var credentialOptions = new ManagedIdentityCredentialOptions(identity);
        credentialOptions.Diagnostics.IsLoggingEnabled = false;
        credentialOptions.Diagnostics.IsLoggingContentEnabled = false;
        credentialOptions.Diagnostics.IsAccountIdentifierLoggingEnabled = false;
        credentialOptions.Retry.MaxRetries = 0;
        credentialOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(10);
        var credential = new ManagedIdentityCredential(credentialOptions);
        signalR.AddAzureSignalR(options =>
        {
            options.ConnectionString = null;
            options.Endpoints = [new ServiceEndpoint(endpoint, credential)];
            options.ApplicationName = "TaskManager";
            options.AccessTokenLifetime = TimeSpan.FromMinutes(5);
            options.ClaimsProvider = context => context.User.Claims.Where(claim => claim.Type is
                ClaimTypes.NameIdentifier or "AspNet.Identity.SecurityStamp");
        });
        return services;
    }
}
