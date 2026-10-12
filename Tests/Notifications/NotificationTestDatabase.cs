using DAL.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Tests.Notifications;

// Shared by SQL concurrency and actual broker→SQL tests. The schema is migrated,
// never reset from EnsureCreated or borrowed from an existing database.
internal sealed class NotificationTestDatabase(string serverConnection, string name) : IAsyncDisposable
{
    private const string Prefix = "taskmanager_notification_test_";
    public string ConnectionString { get; } = new SqlConnectionStringBuilder(serverConnection) { InitialCatalog = name }.ConnectionString;
    public static async Task<NotificationTestDatabase> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable("TASKMANAGER_TEST_SQL_SERVER");
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("The isolated SQL test server is not configured.");
        var server = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        var name = Prefix + Guid.NewGuid().ToString("N"); Validate(name);
        await using var connection = new SqlConnection(server.ConnectionString); await connection.OpenAsync();
        await using var create = new SqlCommand($"CREATE DATABASE [{name}]", connection); await create.ExecuteNonQueryAsync();
        var database = new NotificationTestDatabase(server.ConnectionString, name);
        try
        {
            await using var db = new TaskContext(new DbContextOptionsBuilder<TaskContext>().UseSqlServer(database.ConnectionString).Options);
            await db.Database.MigrateAsync();
            return database;
        }
        catch { await database.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        Validate(name);
        using (var owned = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(owned);
        await using var connection = new SqlConnection(serverConnection); await connection.OpenAsync();
        await using var drop = new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", connection);
        await drop.ExecuteNonQueryAsync();
    }
    private static void Validate(string candidate)
    {
        if (!candidate.StartsWith(Prefix, StringComparison.Ordinal) || candidate.Length != Prefix.Length + 32
            || !Guid.TryParseExact(candidate[Prefix.Length..], "N", out _))
            throw new InvalidOperationException("Refusing operations outside the disposable notification test database namespace.");
    }
}
