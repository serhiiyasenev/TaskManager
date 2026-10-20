using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace DAL.Extensions;

public static class AzureSqlConnectionPolicy
{
    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("DbConnection") ?? "";
        if (configuration["Database:Authentication"] != "AzureManagedIdentity") return connection;
        try
        {
            var builder = new SqlConnectionStringBuilder(connection);
            if (builder.Authentication != SqlAuthenticationMethod.ActiveDirectoryManagedIdentity ||
                builder.IntegratedSecurity || !string.IsNullOrEmpty(builder.Password) ||
                !Guid.TryParse(builder.UserID, out var clientId) || clientId == Guid.Empty ||
                builder.Encrypt == SqlConnectionEncryptOption.Optional || builder.TrustServerCertificate ||
                !string.IsNullOrEmpty(builder.HostNameInCertificate) || !string.IsNullOrEmpty(builder.ServerCertificate) ||
                !Regex.IsMatch(builder.DataSource, "^tcp:[a-z0-9-]+\\.database\\.windows\\.net,1433$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                string.IsNullOrWhiteSpace(builder.InitialCatalog) || builder.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Azure SQL requires a dedicated Azure database, explicit managed identity client GUID and validated encrypted transport.");
            return connection;
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Azure SQL managed identity connection configuration is invalid.");
        }
    }
}
