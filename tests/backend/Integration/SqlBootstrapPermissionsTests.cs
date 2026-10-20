using System.Text.Json;
using DAL.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskManager.SqlBootstrap;
using Tests.Notifications;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Integration;

public sealed class SqlBootstrapPermissionsTests
{
    private static readonly SqlBootstrapPrincipal[] Principals =
    [
        new("taskmanager-api", Guid.Parse("11111111-1111-4111-8111-111111111111"), Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), SqlWorkloadRole.Api),
        new("taskmanager-notifier", Guid.Parse("22222222-2222-4222-8222-222222222222"), Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), SqlWorkloadRole.Notifier),
        new("taskmanager-migrator", Guid.Parse("33333333-3333-4333-8333-333333333333"), Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"), SqlWorkloadRole.Migration)
    ];
    private static string Mapping() => JsonSerializer.Serialize(Principals.Select(p => new
        { name = p.Name, objectId = p.ObjectId, clientId = p.ClientId, role = p.Role.ToString() }));

    [Theory]
    [InlineData("Host Name In Certificate=other.database.windows.net")]
    [InlineData("Server Certificate=private-certificate.pem")]
    public void BootstrapRejectsCertificateIdentityOverrides(string setting)
    {
        var connection = "Server=tcp:taskmanager.database.windows.net,1433;Database=taskmanager;Authentication=Active Directory Managed Identity;"
            + "User Id=dddddddd-dddd-4ddd-8ddd-dddddddddddd;Encrypt=True;TrustServerCertificate=False;" + setting;
        Assert.Throws<InvalidOperationException>(() => SqlBootstrapConnectionPolicy.Validate(connection, Principals));
    }

    [SqlServerFact]
    public async Task NativeSql_VerificationRejectsExistingExcessiveRoleAndDirectMembership()
    {
        await using var database = await NotificationTestDatabase.CreateAsync();
        await using var connection = new SqlConnection(database.ConnectionString); await connection.OpenAsync();
        var notifier = Principals.Single(p => p.Role == SqlWorkloadRole.Notifier);
        await ExecuteAsync(connection, $"CREATE USER [{notifier.Name}] WITHOUT LOGIN;");
        await ExecuteAsync(connection, SqlBootstrapPlan.GrantSql(notifier, true));
        await ExecuteAsync(connection, $"EXECUTE AS USER = '{notifier.Name}';");
        try { Assert.Equal(0, await ScalarAsync(connection, SqlBootstrapPlan.ExcessAuthoritySql(notifier))); }
        finally { await ExecuteAsync(connection, "REVERT;"); }

        await ExecuteAsync(connection, "GRANT UPDATE ON dbo.Tasks TO taskmanager_notifier;");
        await ExecuteAsync(connection, $"EXECUTE AS USER = '{notifier.Name}';");
        try
        {
            // This is an effective permission, not merely role-name metadata.
            await ExecuteAsync(connection, "BEGIN TRANSACTION; UPDATE dbo.Tasks SET Name=Name; ROLLBACK TRANSACTION;");
            Assert.Equal(1, await ScalarAsync(connection, SqlBootstrapPlan.ExcessAuthoritySql(notifier)));
        }
        finally { await ExecuteAsync(connection, "REVERT;"); }

        await ExecuteAsync(connection, "REVOKE UPDATE ON dbo.Tasks FROM taskmanager_notifier; ALTER ROLE db_datawriter ADD MEMBER [taskmanager-notifier];");
        await ExecuteAsync(connection, $"EXECUTE AS USER = '{notifier.Name}';");
        try { Assert.Equal(1, await ScalarAsync(connection, SqlBootstrapPlan.ExcessAuthoritySql(notifier))); }
        finally { await ExecuteAsync(connection, "REVERT;"); }
    }

    [Fact]
    public void BootstrapUsesThreeDistinctManagedIdentityMappings_AndClientSid()
    {
        Assert.Equal(Principals, SqlBootstrapPlan.ParseUsers(Mapping()));
        var sql = SqlBootstrapPlan.CreatePrincipalSql(Principals[0]);
        Assert.Contains("0x" + Convert.ToHexString(Principals[0].ClientId.ToByteArray()), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToHexString(Principals[0].ObjectId.ToByteArray()), sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TYPE = E", sql);
        Assert.DoesNotContain("FROM EXTERNAL PROVIDER", sql);
        Assert.DoesNotContain("ALTER USER", sql);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{}]")]
    [InlineData("{\"users\":[]}")]
    public void InvalidMapping_FailsBeforeDatabaseAccess(string mapping) =>
        Assert.Throws<InvalidOperationException>(() => SqlBootstrapPlan.ParseUsers(mapping));

    [Theory]
    [InlineData("taskmanager-api] ; DROP DATABASE [demo] --")]
    [InlineData("dbo")]
    [InlineData("taskmanager api")]
    public void UnsafePrincipalName_IsRejected(string name)
    {
        var mapping = Mapping().Replace("taskmanager-api", name, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => SqlBootstrapPlan.ParseUsers(mapping));
    }

    [Fact]
    public void DuplicateAuthorityAndUnknownProperties_AreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => SqlBootstrapPlan.ParseUsers(Mapping()
            .Replace(Principals[1].ClientId.ToString(), Principals[0].ClientId.ToString(), StringComparison.Ordinal)));
        Assert.Throws<InvalidOperationException>(() => SqlBootstrapPlan.ParseUsers(Mapping()
            .Replace("\"role\":\"Api\"", "\"role\":\"Api\",\"role\":\"Migration\"", StringComparison.Ordinal)));
        Assert.Throws<InvalidOperationException>(() => SqlBootstrapPlan.ParseUsers(Mapping()
            .Replace("\"role\":\"Api\"", "\"role\":\"Api\",\"admin\":true", StringComparison.Ordinal)));
    }

    [SqlServerFact]
    public async Task NativeSql_AppRolesAllowWorkAndForbidDdlPrivilegeEscalationAndMigrationHistory()
    {
        await using var database = await NotificationTestDatabase.CreateAsync();
        await using var connection = new SqlConnection(database.ConnectionString); await connection.OpenAsync();
        foreach (var principal in Principals)
        {
            // Native SQL fixture tests real SQL permissions with non-login users.
            // It does not claim Azure Entra SID authentication was exercised.
            await ExecuteAsync(connection, $"CREATE USER [{principal.Name}] WITHOUT LOGIN;");
            await ExecuteAsync(connection, SqlBootstrapPlan.GrantSql(principal, schemaReady: true));
        }
        foreach (var principal in Principals.Where(p => p.Role != SqlWorkloadRole.Migration))
        {
            await ExecuteAsync(connection, $"EXECUTE AS USER = '{principal.Name}';");
            try
            {
                Assert.Equal(0, await ScalarAsync(connection, SqlBootstrapPlan.ExcessAuthoritySql(principal)));
                Assert.Equal(1, await ScalarAsync(connection, "SELECT HAS_PERMS_BY_NAME('dbo.Tasks', 'OBJECT', 'SELECT')"));
                Assert.Equal(0, await ScalarAsync(connection, "SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE')"));
                Assert.Equal(0, await ScalarAsync(connection, "SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY USER')"));
                Assert.Equal(0, await ScalarAsync(connection, "SELECT HAS_PERMS_BY_NAME('dbo.__EFMigrationsHistory', 'OBJECT', 'UPDATE')"));
                Assert.Equal(0, await ScalarAsync(connection, "SELECT IS_ROLEMEMBER('db_owner')"));
                await ExecuteAsync(connection, "SELECT TOP(1) Id FROM dbo.Tasks;");
                await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, "CREATE TABLE dbo.UnauthorizedTable (Id int);"));
                await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, "UPDATE dbo.__EFMigrationsHistory SET ProductVersion = 'unauthorized';"));
                await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, "ALTER ROLE db_owner ADD MEMBER [taskmanager-api];"));
                if (principal.Role == SqlWorkloadRole.Api)
                {
                    await ExecuteAsync(connection, "BEGIN TRANSACTION; UPDATE dbo.Tasks SET Name = Name; ROLLBACK TRANSACTION;");
                    await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, "INSERT INTO dbo.NotificationProcessingMarkers(MessageId,CanonicalPayloadHash,ProcessedAtUtc,Disposition) VALUES(NEWID(),REPLICATE('a',64),GETUTCDATE(),0);"));
                }
                else
                {
                    await ExecuteAsync(connection, "BEGIN TRANSACTION; UPDATE dbo.NotificationOutbox SET Status = Status; UPDATE dbo.Tasks SET ReminderSentAt = ReminderSentAt; DELETE FROM dbo.AiDailyUsage WHERE 1=0; ROLLBACK TRANSACTION;");
                    await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, "UPDATE dbo.Tasks SET Name = 'unauthorized';"));
                    await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, "DELETE FROM dbo.Teams;"));
                }
            }
            finally { await ExecuteAsync(connection, "REVERT;"); }
        }
        await ExecuteAsync(connection, "EXECUTE AS USER = 'taskmanager-migrator';");
        try
        {
            await ExecuteAsync(connection, "CREATE TABLE dbo.VerifiedMigrationDdl(Id int NOT NULL PRIMARY KEY); ALTER TABLE dbo.VerifiedMigrationDdl ADD Revision int NULL; DROP TABLE dbo.VerifiedMigrationDdl;");
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, "ALTER ROLE db_owner ADD MEMBER [taskmanager-api];"));
        }
        finally { await ExecuteAsync(connection, "REVERT;"); }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    { await using var command = new SqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
    private static async Task<int> ScalarAsync(SqlConnection connection, string sql)
    { await using var command = new SqlCommand(sql, connection); return Convert.ToInt32(await command.ExecuteScalarAsync()); }

    [SqlServerFact]
    public async Task NativeSql_MigratorCanApplyRealForwardMigrationsWithoutDatabaseOwnerOrSecurityAdmin()
    {
        await using var database = await NotificationTestDatabase.CreateAsync();
        await using (var administrator = new TaskContext(new DbContextOptionsBuilder<TaskContext>().UseSqlServer(database.ConnectionString).Options))
            await administrator.GetService<IMigrator>().MigrateAsync("20261011175103_SecureLegacyAccounts");
        await using var connection = new SqlConnection(database.ConnectionString); await connection.OpenAsync();
        var migrator = Principals.Single(p => p.Role == SqlWorkloadRole.Migration);
        await ExecuteAsync(connection, $"CREATE USER [{migrator.Name}] WITHOUT LOGIN;");
        await ExecuteAsync(connection, SqlBootstrapPlan.GrantSql(migrator, schemaReady: false));
        await ExecuteAsync(connection, $"EXECUTE AS USER = '{migrator.Name}';");
        try
        {
            Assert.Equal(0, await ScalarAsync(connection, "SELECT IS_ROLEMEMBER('db_owner')"));
            Assert.Equal(0, await ScalarAsync(connection, "SELECT HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','ALTER ANY USER')"));
            await using var context = new TaskContext(new DbContextOptionsBuilder<TaskContext>().UseSqlServer(connection).Options);
            await context.Database.MigrateAsync();
            Assert.Equal(8, (await context.Database.GetAppliedMigrationsAsync()).Count());
            await using var identity = new SqlCommand("SELECT USER_NAME()", connection);
            Assert.Equal(migrator.Name, await identity.ExecuteScalarAsync());
        }
        finally { await ExecuteAsync(connection, "REVERT;"); }
    }
}
