using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace TaskManager.SqlBootstrap;

public static class SqlBootstrapConnectionPolicy
{
    public static void Validate(string connectionString, IReadOnlyList<SqlBootstrapPrincipal> users)
    {
        var settings = new SqlConnectionStringBuilder(connectionString);
        if (settings.Authentication != SqlAuthenticationMethod.ActiveDirectoryManagedIdentity
            || !Guid.TryParse(settings.UserID, out var identity) || identity == Guid.Empty
            || users.Any(user => user.ClientId == identity) || settings.IntegratedSecurity
            || !string.IsNullOrEmpty(settings.Password) || settings.PersistSecurityInfo || settings.TrustServerCertificate
            || !string.IsNullOrEmpty(settings.HostNameInCertificate) || !string.IsNullOrEmpty(settings.ServerCertificate)
            || settings.Encrypt == SqlConnectionEncryptOption.Optional || !string.IsNullOrEmpty(settings.AttachDBFilename)
            || !Regex.IsMatch(settings.InitialCatalog, "^[a-zA-Z][a-zA-Z0-9_-]{0,99}$", RegexOptions.CultureInvariant)
            || new[] { "master", "model", "msdb", "tempdb" }.Contains(settings.InitialCatalog, StringComparer.OrdinalIgnoreCase)
            || !Regex.IsMatch(settings.DataSource, "^tcp:[a-z0-9-]+\\.database\\.windows\\.net,1433$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Explicit Azure SQL admin identity connection required.");
    }
}
