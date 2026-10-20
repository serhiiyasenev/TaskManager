using Azure.Core;
using Azure.Core.Cryptography;
using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaskManager.Hosting.Cloud;

public sealed class CloudDataProtectionOptions
{
    public string Provider { get; set; } = "FileSystem";
    public string? KeyRingPath { get; set; }
    public string? BlobUri { get; set; }
    public string? KeyVaultKeyUri { get; set; }
    public string? ManagedIdentityClientId { get; set; }
}

/// <summary>Native SDK clients; tests replace this seam without a public fake provider.</summary>
public interface ICloudDataProtectionClients
{
    BlobClient GetBlobClient(CloudDataProtectionOptions options);
    IKeyEncryptionKeyResolver GetKeyResolver(CloudDataProtectionOptions options);
}

public static class CloudManagedIdentity
{
    public static TokenCredential Create(string? clientId)
    {
        ValidateClientId(clientId);
        return new ManagedIdentityCredential(string.IsNullOrWhiteSpace(clientId)
            ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(clientId));
    }

    public static void ValidateClientId(string? clientId)
    {
        if (!string.IsNullOrWhiteSpace(clientId) && (!Guid.TryParse(clientId, out var id) || id == Guid.Empty))
            throw new InvalidOperationException("ManagedIdentityClientId must be an explicit nonempty client GUID.");
    }
}

public static class CloudDataProtection
{
    public const string ApplicationName = "TaskManager";

    public static IServiceCollection AddTaskManagerDataProtection(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<CloudDataProtectionOptions>().Bind(configuration.GetSection("DataProtection"))
            .Configure(options =>
            {
                if (options.Provider is "FileSystem" or "AzureBlobKeyVault") Validate(options, environment);
            })
            .Validate(options => options.Provider is "FileSystem" or "AzureBlobKeyVault",
                "DataProtection:Provider is not implemented.").ValidateOnStart();
        services.TryAddSingleton<ICloudDataProtectionClients, AzureDataProtectionClients>();
        services.AddDataProtection().SetApplicationName(ApplicationName);

        // Native SDK configuration stays conditional until the final options resolve.
        // The SDK owns XML storage, key wrapping and decryptor activation.
        var azureRegistrations = new ServiceCollection();
        azureRegistrations.AddDataProtection()
            .PersistKeysToAzureBlobStorage(sp => sp.GetRequiredService<ICloudDataProtectionClients>()
                .GetBlobClient(sp.GetRequiredService<IOptions<CloudDataProtectionOptions>>().Value))
            .ProtectKeysWithAzureKeyVault(sp => sp.GetRequiredService<IOptions<CloudDataProtectionOptions>>().Value.KeyVaultKeyUri!,
                sp => CloudManagedIdentity.Create(sp.GetRequiredService<IOptions<CloudDataProtectionOptions>>().Value.ManagedIdentityClientId));
        foreach (var descriptor in azureRegistrations)
        {
            var sdkType = descriptor.ImplementationType ?? descriptor.ServiceType;
            if (sdkType.Assembly.GetName().Name?.StartsWith("Azure.", StringComparison.Ordinal) != true) continue;
            if (descriptor.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>))
            {
                services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
                    new ConfigureOptions<KeyManagementOptions>(options =>
                    {
                        if (sp.GetRequiredService<IOptions<CloudDataProtectionOptions>>().Value.Provider != "AzureBlobKeyVault") return;
                        var sdkConfiguration = (IConfigureOptions<KeyManagementOptions>)
                            (descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(sp) ??
                             ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
                        sdkConfiguration.Configure(options);
                    }));
            }
            else services.Add(descriptor);
        }
        services.AddSingleton<IKeyEncryptionKeyResolver>(sp => sp.GetRequiredService<ICloudDataProtectionClients>()
            .GetKeyResolver(sp.GetRequiredService<IOptions<CloudDataProtectionOptions>>().Value));
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<CloudDataProtectionOptions>, ILoggerFactory>((options, cloud, logger) =>
        {
            if (cloud.Value.Provider == "FileSystem" && !string.IsNullOrWhiteSpace(cloud.Value.KeyRingPath))
                options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(cloud.Value.KeyRingPath), logger);
        });
        return services;
    }

    private static void Validate(CloudDataProtectionOptions options, IHostEnvironment environment)
    {
        if (options.Provider == "FileSystem")
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") &&
                (string.IsNullOrWhiteSpace(options.KeyRingPath) || !Path.IsPathFullyQualified(options.KeyRingPath)))
                throw new InvalidOperationException("Production requires an absolute persistent DataProtection:KeyRingPath.");
            return;
        }
        if (!string.IsNullOrWhiteSpace(options.KeyRingPath))
            throw new InvalidOperationException("Azure data protection cannot also configure a filesystem key ring.");
        CloudManagedIdentity.ValidateClientId(options.ManagedIdentityClientId);
        ValidateAzureUri(options.BlobUri, ".blob.core.windows.net", 2, "DataProtection:BlobUri");
        var keyUri = ValidateAzureUri(options.KeyVaultKeyUri, ".vault.azure.net", 2, "DataProtection:KeyVaultKeyUri");
        if (keyUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[0] != "keys")
            throw new InvalidOperationException("DataProtection:KeyVaultKeyUri must identify a versionless key.");
    }

    public static Uri ValidateAzureUri(string? value, string suffix, int pathSegments, string optionName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.IdnHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || uri.IdnHost.Length <= suffix.Length ||
            uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length != pathSegments ||
            uri.AbsolutePath.Contains('%') || uri.AbsolutePath.Contains("//", StringComparison.Ordinal))
            throw new InvalidOperationException($"{optionName} must be an explicit HTTPS Azure resource URI without credentials, query or fragment.");
        return uri;
    }
}

internal sealed class AzureDataProtectionClients : ICloudDataProtectionClients
{
    public BlobClient GetBlobClient(CloudDataProtectionOptions options)
    {
        var clientOptions = new BlobClientOptions();
        clientOptions.Diagnostics.IsLoggingEnabled = false;
        clientOptions.Diagnostics.IsLoggingContentEnabled = false;
        return new BlobClient(new Uri(options.BlobUri!), CloudManagedIdentity.Create(options.ManagedIdentityClientId), clientOptions);
    }

    public IKeyEncryptionKeyResolver GetKeyResolver(CloudDataProtectionOptions options)
    {
        var clientOptions = new CryptographyClientOptions();
        clientOptions.Diagnostics.IsLoggingEnabled = false;
        clientOptions.Diagnostics.IsLoggingContentEnabled = false;
        return new KeyResolver(CloudManagedIdentity.Create(options.ManagedIdentityClientId), clientOptions);
    }
}
