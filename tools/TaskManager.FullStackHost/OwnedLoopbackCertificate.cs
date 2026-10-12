using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TaskManager.FullStackHost;

internal sealed class OwnedLoopbackCertificate : IDisposable
{
    private readonly RSA _rsa;
    private readonly CngKey? _windowsKey;
    private readonly string _keyName;
    private bool _disposed;
    public X509Certificate2 Certificate { get; }

    public OwnedLoopbackCertificate(Guid runId)
    {
        _keyName = "taskmanager-e2e-test-" + runId.ToString("N");
        // Schannel cannot use an EphemeralKeySet certificate reliably. Give
        // this one test certificate a uniquely named current-user CNG key,
        // explicitly deleted after listener shutdown. No certificate is added
        // to any X509Store and no trust/profile/machine setting is changed.
        if (OperatingSystem.IsWindows())
        {
            var parameters = new CngKeyCreationParameters
            {
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                KeyUsage = CngKeyUsages.Signing | CngKeyUsages.Decryption,
                ExportPolicy = CngExportPolicies.None
            };
            parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
            _windowsKey = CngKey.Create(CngAlgorithm.Rsa, _keyName, parameters);
            try { _rsa = new RSACng(_windowsKey); }
            catch { _windowsKey.Delete(); _windowsKey.Dispose(); throw; }
        }
        else _rsa = RSA.Create(2048);
        try
        {
            var request = new CertificateRequest("CN=127.0.0.1", _rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Loopback); names.AddDnsName("localhost");
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            var usage = new OidCollection { new("1.3.6.1.5.5.7.3.1") };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usage, false));
            Certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(8));
        }
        catch
        {
            _rsa.Dispose();
            if (OperatingSystem.IsWindows() && _windowsKey is not null) { _windowsKey.Delete(); _windowsKey.Dispose(); }
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Certificate.Dispose();
        _rsa.Dispose();
        if (OperatingSystem.IsWindows() && _windowsKey is not null)
        {
            _windowsKey.Delete();
            _windowsKey.Dispose();
            if (CngKey.Exists(_keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.None))
                throw new InvalidOperationException("Owned loopback private-key cleanup was not verified.");
        }
        _disposed = true;
    }
}
