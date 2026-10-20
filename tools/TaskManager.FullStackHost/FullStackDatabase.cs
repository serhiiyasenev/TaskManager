using System.Text.RegularExpressions;
using DAL.Context;
using NotificationOutboxStatus = DAL.Entities.NotificationOutboxStatus;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace TaskManager.FullStackHost;

internal sealed class FullStackDatabase(FullStackSettings settings)
{
    private bool _created;
    public int MigrationCount { get; private set; }
    public string ConnectionString => new SqlConnectionStringBuilder(settings.MasterConnection)
        { InitialCatalog = settings.DatabaseName }.ConnectionString;

    public TaskContext CreateContext() => new(new DbContextOptionsBuilder<TaskContext>().UseSqlServer(ConnectionString).Options);

    public async System.Threading.Tasks.Task CreateAsync(CancellationToken ct)
    {
        ValidateName(settings.DatabaseName);
        await using var master = new SqlConnection(settings.MasterConnection);
        await master.OpenAsync(ct);
        await using (var command = master.CreateCommand())
        {
            command.CommandTimeout = 60;
            command.CommandText = $"CREATE DATABASE [{settings.DatabaseName}]";
            await command.ExecuteNonQueryAsync(ct);
            _created = true;
        }
        await using var db = CreateContext();
        if (db.Database.GetMigrations().Count() != 8)
            throw new InvalidOperationException("Full-stack verification expects the eight accepted native migrations.");
        await db.Database.MigrateAsync(ct);
        MigrationCount = (await db.Database.GetAppliedMigrationsAsync(ct)).Count();
        if (MigrationCount != 8) throw new InvalidOperationException("Full-stack SQL migration count is invalid.");
    }

    public async Task<object> GetSummaryAsync(long acknowledged, CancellationToken ct)
    {
        await using var db = CreateContext();
        // Counts only: no private task content, account directory or credentials.
        return new
        {
            migrationCount = MigrationCount,
            tasks = await db.Tasks.CountAsync(ct),
            notifications = await db.UserNotifications.CountAsync(ct),
            processingMarkers = await db.NotificationProcessingMarkers.CountAsync(ct),
            materializations = await db.AiTaskMaterializations.CountAsync(ct),
            commitReceipts = await db.AiTaskCommitReceipts.CountAsync(ct),
            outboxPublished = await db.NotificationOutbox.CountAsync(value => value.Status == NotificationOutboxStatus.Published, ct),
            outboxPending = await db.NotificationOutbox.CountAsync(value => value.Status == NotificationOutboxStatus.Pending, ct),
            acknowledged
        };
    }

    public async System.Threading.Tasks.Task AssertCommittedMarkerAsync(Guid messageId, CancellationToken ct)
    {
        await using var db = CreateContext();
        if (!await db.NotificationProcessingMarkers.AsNoTracking().AnyAsync(value => value.MessageId == messageId, ct))
            throw new InvalidOperationException("A notification cannot settle before its SQL processing marker commits.");
    }

    public async System.Threading.Tasks.Task DropAsync(CancellationToken ct)
    {
        ValidateName(settings.DatabaseName);
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(settings.MasterConnection);
        await master.OpenAsync(ct);
        await using var command = master.CreateCommand();
        command.CommandTimeout = 60;
        if (!_created)
        {
            command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @ownedName";
            command.Parameters.AddWithValue("@ownedName", settings.DatabaseName);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 0)
                throw new InvalidOperationException("An existing database was not created by this host and cannot be deleted.");
            return;
        }
        command.CommandText = $"ALTER DATABASE [{settings.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{settings.DatabaseName}];";
        await command.ExecuteNonQueryAsync(ct);
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @ownedName";
        command.Parameters.AddWithValue("@ownedName", settings.DatabaseName);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 0)
            throw new InvalidOperationException("Owned full-stack database cleanup was not verified.");
        _created = false;
    }

    private static void ValidateName(string name)
    {
        const string prefix = "taskmanager_e2e_test_";
        if (!Regex.IsMatch(name, "^taskmanager_e2e_test_[a-f0-9]{32}$", RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)) || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
            throw new InvalidOperationException("Only a freshly generated owned full-stack database is allowed.");
    }
}
