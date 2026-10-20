using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Azure;
using Azure.Core.Cryptography;
using Azure.Core;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using TaskManager.Hosting.Cloud;
using Xunit;

namespace Tests.Cloud;

public sealed class CloudDataProtectionTests
{
    [Fact]
    public void AzureKeyRing_RestartedHostDecryptsUsingWrappedPersistedKey()
    {
        using var fixture = new NativeKeyRingFixture();
        string encrypted;
        using (var first = Build(fixture))
            encrypted = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("session-test").Protect("synthetic-cookie");

        using var restarted = Build(fixture);
        Assert.Equal("synthetic-cookie", restarted.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("session-test").Unprotect(encrypted));
        Assert.Contains("encryptedKey", fixture.Xml);
        Assert.Contains("https://taskmanager-test.vault.azure.net/keys/keyring/version-test", fixture.Xml);
        Assert.DoesNotContain("synthetic-cookie", fixture.Xml);
        Assert.True(fixture.WrapCalls > 0);
        Assert.True(fixture.UnwrapCalls > 0);
    }

    [Theory]
    [InlineData("blob")]
    [InlineData("key")]
    public void AzureFailure_DoesNotCreateUsableEphemeralOrFileSystemKey(string failure)
    {
        using var fixture = new NativeKeyRingFixture { DenyBlob = failure == "blob", DenyKey = failure == "key" };
        using var provider = Build(fixture);
        Assert.Throws<CryptographicException>(() => provider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("session-test").Protect("synthetic-cookie"));
        Assert.True(string.IsNullOrEmpty(fixture.Xml));
    }

    [Fact]
    public void DifferentApplicationCannotDecryptSharedBlob()
    {
        using var fixture = new NativeKeyRingFixture();
        using var first = Build(fixture);
        var encrypted = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("session-test").Protect("synthetic-cookie");
        using var second = Build(fixture, services => services.AddDataProtection().SetApplicationName("another-product"));
        Assert.Throws<CryptographicException>(() => second.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("session-test").Unprotect(encrypted));
    }

    [Theory]
    [InlineData("DataProtection:BlobUri", "http://taskmanager-test.blob.core.windows.net/keys/keyring.xml")]
    [InlineData("DataProtection:BlobUri", "https://taskmanager-test.blob.core.windows.net/keys/keyring.xml?sig=secret")]
    [InlineData("DataProtection:BlobUri", "https://user:secret@taskmanager-test.blob.core.windows.net/keys/keyring.xml")]
    [InlineData("DataProtection:BlobUri", "https://arbitrary.example.test/keys/keyring.xml")]
    [InlineData("DataProtection:KeyVaultKeyUri", "https://taskmanager-test.vault.azure.net/keys/keyring/version")]
    [InlineData("DataProtection:KeyVaultKeyUri", "https://taskmanager-test.vault.azure.net/secrets/keyring")]
    [InlineData("DataProtection:ManagedIdentityClientId", "not-a-client-id")]
    [InlineData("DataProtection:KeyRingPath", "C:/fallback-keyring")]
    public void InvalidAzureConfigurationFailsBeforeCredentialOrStorageCall(string key, string value)
    {
        using var fixture = new NativeKeyRingFixture();
        var error = Assert.ThrowsAny<InvalidOperationException>(() =>
        {
            using var provider = Build(fixture, values: new() { [key] = value });
            provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("test");
        });
        Assert.DoesNotContain("sig=secret", error.Message);
        Assert.Equal(0, fixture.ClientCalls);
    }

    [Fact]
    public void UnsupportedProviderUsesNativeOptionsValidationBeforeAnyClientCall()
    {
        using var fixture = new NativeKeyRingFixture();
        var error = Assert.Throws<OptionsValidationException>(() =>
        {
            using var provider = Build(fixture, values: new() { ["DataProtection:Provider"] = "unimplemented" });
            provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("test");
        });
        Assert.Contains("DataProtection:Provider is not implemented", error.Message);
        Assert.Equal(0, fixture.ClientCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("5992d8df-3e71-401e-9864-ccfa1cfdac22")]
    public void Credential_IsExplicitManagedIdentityWithoutAmbientFallback(string? clientId) =>
        Assert.IsType<ManagedIdentityCredential>(CloudManagedIdentity.Create(clientId));

    internal static ServiceProvider Build(NativeKeyRingFixture fixture,
        Action<IServiceCollection>? extra = null, Dictionary<string, string?>? values = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:Provider"] = "AzureBlobKeyVault",
            ["DataProtection:BlobUri"] = "https://taskmanager-test.blob.core.windows.net/keys/keyring.xml",
            ["DataProtection:KeyVaultKeyUri"] = "https://taskmanager-test.vault.azure.net/keys/keyring",
            ["DataProtection:ManagedIdentityClientId"] = "5992d8df-3e71-401e-9864-ccfa1cfdac22"
        };
        foreach (var pair in values ?? []) settings[pair.Key] = pair.Value;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICloudDataProtectionClients>(fixture);
        services.AddTaskManagerDataProtection(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new CloudTestEnvironment());
        extra?.Invoke(services);
        return services.BuildServiceProvider();
    }
}

internal sealed class CloudTestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
    public string ApplicationName { get; set; } = "CloudTests";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>Synthetic native BlobClient and RSA resolver, exercising the actual DP SDK pipeline.</summary>
internal sealed class NativeKeyRingFixture : ICloudDataProtectionClients, IKeyEncryptionKeyResolver, IKeyEncryptionKey, IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private byte[]? _blob;
    private int _revision;
    public bool DenyBlob { get; init; }
    public bool DenyKey { get; init; }
    public int ClientCalls { get; private set; }
    public int WrapCalls { get; private set; }
    public int UnwrapCalls { get; private set; }
    public string Xml => _blob is null ? "" : Encoding.UTF8.GetString(_blob);
    public string KeyId => "https://taskmanager-test.vault.azure.net/keys/keyring/version-test";

    public BlobClient GetBlobClient(CloudDataProtectionOptions options)
    {
        ClientCalls++;
        var client = new Mock<BlobClient>();
        client.Setup(c => c.DownloadTo(It.IsAny<Stream>(), It.IsAny<BlobRequestConditions>(),
                It.IsAny<StorageTransferOptions>(), It.IsAny<CancellationToken>()))
            .Returns((Stream destination, BlobRequestConditions conditions, StorageTransferOptions transfer, CancellationToken ct) =>
            {
                if (DenyBlob) throw new RequestFailedException(403, "Synthetic forbidden blob.");
                if (_blob is null) throw new RequestFailedException(404, "Synthetic empty blob.");
                var etag = new ETag($"\"{_revision}\"");
                if (conditions?.IfNoneMatch == etag) return ResponseWithTag(304);
                destination.Write(_blob);
                return ResponseWithTag(200);
            });
        client.Setup(c => c.Upload(It.IsAny<Stream>(), It.IsAny<BlobHttpHeaders>(),
                It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobRequestConditions>(), It.IsAny<IProgress<long>>(),
                It.IsAny<AccessTier?>(), It.IsAny<StorageTransferOptions>(), It.IsAny<CancellationToken>()))
            .Returns((Stream source, BlobHttpHeaders headers, IDictionary<string, string> metadata,
                BlobRequestConditions conditions, IProgress<long> progress, AccessTier? tier, StorageTransferOptions transfer, CancellationToken ct) =>
            {
                if (DenyBlob) throw new RequestFailedException(403, "Synthetic forbidden blob.");
                if ((_blob is not null && conditions.IfNoneMatch == ETag.All) ||
                    (conditions.IfMatch is not null && conditions.IfMatch != new ETag($"\"{_revision}\"")))
                    throw new RequestFailedException(412, "Synthetic precondition conflict.");
                using var buffer = new MemoryStream();
                source.CopyTo(buffer);
                _blob = buffer.ToArray();
                _revision++;
                return Response.FromValue(BlobsModelFactory.BlobContentInfo(new ETag($"\"{_revision}\""), DateTimeOffset.UtcNow,
                    [], "synthetic", "synthetic", 0), ResponseWithTag(201));
            });
        return client.Object;
    }

    private Response ResponseWithTag(int status)
    {
        return new NativeBlobResponse(status, $"\"{_revision}\"");
    }

    public IKeyEncryptionKeyResolver GetKeyResolver(CloudDataProtectionOptions options)
    {
        ClientCalls++;
        // Host DI owns the returned client, while this fixture owns the shared RSA across restarts.
        return new NativeResolver(this);
    }
    public IKeyEncryptionKey Resolve(string keyId, CancellationToken cancellationToken = default)
    {
        if (DenyKey) throw new RequestFailedException(403, "Synthetic forbidden key.");
        Assert.True(keyId == KeyId || keyId == "https://taskmanager-test.vault.azure.net/keys/keyring");
        return this;
    }
    public Task<IKeyEncryptionKey> ResolveAsync(string keyId, CancellationToken ct = default) => Task.FromResult(Resolve(keyId, ct));
    public byte[] WrapKey(string algorithm, ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        Assert.Equal("RSA-OAEP", algorithm); WrapCalls++;
        return _rsa.Encrypt(key.Span, RSAEncryptionPadding.OaepSHA1);
    }
    public byte[] UnwrapKey(string algorithm, ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        Assert.Equal("RSA-OAEP", algorithm); UnwrapCalls++;
        return _rsa.Decrypt(key.Span, RSAEncryptionPadding.OaepSHA1);
    }
    public Task<byte[]> WrapKeyAsync(string algorithm, ReadOnlyMemory<byte> key, CancellationToken ct = default) => Task.FromResult(WrapKey(algorithm, key, ct));
    public Task<byte[]> UnwrapKeyAsync(string algorithm, ReadOnlyMemory<byte> key, CancellationToken ct = default) => Task.FromResult(UnwrapKey(algorithm, key, ct));
    public void Dispose() => _rsa.Dispose();

    private sealed class NativeResolver(NativeKeyRingFixture fixture) : IKeyEncryptionKeyResolver
    {
        public IKeyEncryptionKey Resolve(string keyId, CancellationToken ct = default) => fixture.Resolve(keyId, ct);
        public Task<IKeyEncryptionKey> ResolveAsync(string keyId, CancellationToken ct = default) => fixture.ResolveAsync(keyId, ct);
    }
}

internal sealed class NativeBlobResponse(int status, string etag) : Response
{
    public override int Status => status;
    public override string ReasonPhrase => "Synthetic SDK response";
    public override Stream? ContentStream { get; set; }
    public override string ClientRequestId { get; set; } = "synthetic";
    public override void Dispose() => ContentStream?.Dispose();
    protected override bool ContainsHeader(string name) => name.Equals("ETag", StringComparison.OrdinalIgnoreCase);
    protected override IEnumerable<HttpHeader> EnumerateHeaders() => [new HttpHeader("ETag", etag)];
    protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
    {
        value = ContainsHeader(name) ? etag : null;
        return value is not null;
    }
    protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
    {
        values = ContainsHeader(name) ? [etag] : null;
        return values is not null;
    }
}
