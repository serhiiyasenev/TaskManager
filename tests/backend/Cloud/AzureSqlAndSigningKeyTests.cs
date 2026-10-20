using BLL;
using DAL.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Cloud;

public sealed class AzureSqlAndSigningKeyTests
{
    private const string Valid = "Server=tcp:taskmanager-test.database.windows.net,1433;Database=taskmanager_test;Authentication=Active Directory Managed Identity;User ID=5992d8df-3e71-401e-9864-ccfa1cfdac22;Encrypt=True;TrustServerCertificate=False";

    [Fact]
    public void ExplicitAzureManagedIdentityPreservesNativeClientIdConnectionContract() =>
        Assert.Equal(Valid, AzureSqlConnectionPolicy.ResolveConnectionString(Config(Valid)));

    [Theory]
    [InlineData("Server=localhost;Database=local;User Id=sa;Password=synthetic-local-secret;TrustServerCertificate=true")]
    [InlineData("Server=sql;Database=compose;User Id=sa;Password=synthetic-local-secret;Encrypt=false")]
    public void NonAzureModePreservesExistingLocalSqlAuthentication(string connection) =>
        Assert.Equal(connection, AzureSqlConnectionPolicy.ResolveConnectionString(Config(connection, null)));

    [Theory]
    [InlineData("Authentication=Active Directory Default")]
    [InlineData("Authentication=Active Directory Interactive")]
    [InlineData("Authentication=Sql Password;User ID=sa;Password=synthetic-secret")]
    [InlineData("Authentication=Active Directory Managed Identity;User ID=not-a-guid")]
    [InlineData("Authentication=Active Directory Managed Identity;User ID=5992d8df-3e71-401e-9864-ccfa1cfdac22;TrustServerCertificate=true")]
    [InlineData("Authentication=Active Directory Managed Identity;User ID=5992d8df-3e71-401e-9864-ccfa1cfdac22;Encrypt=false")]
    public void AzureModeRejectsWeakerOrAmbientAuthentication(string authentication)
    {
        var error = Assert.Throws<InvalidOperationException>(() => AzureSqlConnectionPolicy.ResolveConnectionString(
            Config($"Server=tcp:taskmanager-test.database.windows.net,1433;Database=taskmanager_test;{authentication}")));
        Assert.DoesNotContain("synthetic-secret", error.Message);
    }

    [Theory]
    [InlineData("localhost", "taskmanager_test")]
    [InlineData("tcp:taskmanager-test.database.windows.net,1433", "master")]
    [InlineData("tcp:taskmanager-test.database.windows.net,1433", "")]
    public void AzureModeRequiresDedicatedAzureDatabase(string server, string database) =>
        Assert.Throws<InvalidOperationException>(() => AzureSqlConnectionPolicy.ResolveConnectionString(
            Config($"Server={server};Database={database};Authentication=Active Directory Managed Identity;User ID=5992d8df-3e71-401e-9864-ccfa1cfdac22;Encrypt=true;TrustServerCertificate=false")));

    [Theory]
    [InlineData("@Microsoft.KeyVault(SecretUri=https://synthetic.vault.azure.net/secrets/jwt)")]
    [InlineData(" @microsoft.keyvault(VaultName=synthetic;SecretName=jwt) ")]
    public void UnresolvedPlatformSecretReferenceCannotBecomePredictableJwtSigningKey(string reference)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = reference, ["Jwt:Issuer"] = "synthetic", ["Jwt:Audience"] = "synthetic"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUserIdentityService(configuration);
        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme));
        Assert.DoesNotContain(reference.Trim(), error.Message);
    }

    private static IConfiguration Config(string connection, string? authentication = "AzureManagedIdentity") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DbConnection"] = connection, ["Database:Authentication"] = authentication
        }).Build();
}
