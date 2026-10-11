using DAL.Context;
using User = DAL.Entities.User;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Tests.Integration;

public class LegacyAccountMigrationTests
{
    private const string PreviousMigration = "20261007093000_AddNotificationOutbox";
    private const string SecurityMigration = "20261011175103_SecureLegacyAccounts";

    [Fact]
    public void ForwardRepair_IsDiscoverableAndDoesNotDeleteBusinessOrAccountRows()
    {
        using var db = new TaskContext(new DbContextOptionsBuilder<TaskContext>()
            .UseSqlServer("Server=unused;Database=metadata-only;Integrated Security=True").Options);
        Assert.Contains(SecurityMigration, db.Database.GetMigrations());
        var migration = new DAL.Migrations.SecureLegacyAccounts { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        Assert.DoesNotContain(migration.UpOperations, operation => operation is DeleteDataOperation or DropTableOperation or DropColumnOperation);
        var script = db.GetService<IMigrator>().GenerateScript(PreviousMigration, SecurityMigration);
        Assert.DoesNotContain("DELETE FROM [Tasks]", script);
        Assert.DoesNotContain("DELETE FROM [Projects]", script);
        Assert.DoesNotContain("DELETE FROM [AspNetUsers]", script);
        Assert.DoesNotContain("SET [PasswordHash]", script);
    }

    [SqlServerFact]
    public async Task PreviousSchemaUpgrade_PreservesDataAndHumanAccountsButDisablesSeedCredentials()
    {
        // Explicit opt-in server, disposable uniquely named database owned by this
        // test. Never run migrations against the caller's original database.
        var server = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TASKMANAGER_TEST_SQL_SERVER"))
        {
            InitialCatalog = "master"
        };
        var name = "taskmanager_migration_test_" + Guid.NewGuid().ToString("N");
        var owned = false;
        try
        {
            await using (var connection = new SqlConnection(server.ConnectionString))
            {
                await connection.OpenAsync();
                await using var create = new SqlCommand($"CREATE DATABASE [{name}]", connection);
                await create.ExecuteNonQueryAsync();
                owned = true;
            }
            var database = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name };
            await using var db = new TaskContext(new DbContextOptionsBuilder<TaskContext>()
                .UseSqlServer(database.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            var seedUser = await db.Users.AsNoTracking().SingleAsync(user => user.Id == 1);
            var taskCount = await db.Tasks.CountAsync();
            var projectCount = await db.Projects.CountAsync();
            var userCount = await db.Users.CountAsync();
            var human = new User("human.migration", "Human", "Owner", "human@example.test")
            {
                EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString(), PasswordHash = "synthetic-human-hash-not-a-seed"
            };
            db.Users.Add(human);
            await db.SaveChangesAsync();
            var admin = await db.Roles.SingleAsync(role => role.NormalizedName == "ADMIN");
            db.UserRoles.Add(new IdentityUserRole<int> { UserId = human.Id, RoleId = admin.Id });
            await db.SaveChangesAsync();
            var humanStamp = human.SecurityStamp;

            await migrator.MigrateAsync(SecurityMigration);
            db.ChangeTracker.Clear();

            Assert.Equal(taskCount, await db.Tasks.CountAsync());
            Assert.Equal(projectCount, await db.Projects.CountAsync());
            Assert.Equal(userCount + 1, await db.Users.CountAsync());
            var repaired = await db.Users.SingleAsync(user => user.Id == seedUser.Id);
            Assert.Equal(seedUser.PasswordHash, repaired.PasswordHash);
            Assert.True(repaired.LockoutEnabled);
            Assert.True(repaired.LockoutEnd > DateTimeOffset.UtcNow.AddYears(1));
            Assert.False(repaired.EmailConfirmed);
            Assert.NotEqual(seedUser.SecurityStamp, repaired.SecurityStamp);
            Assert.False(await db.UserRoles.AnyAsync(role => role.UserId == seedUser.Id && role.RoleId == admin.Id));
            var preserved = await db.Users.SingleAsync(user => user.Id == human.Id);
            Assert.Equal(humanStamp, preserved.SecurityStamp);
            Assert.Equal("synthetic-human-hash-not-a-seed", preserved.PasswordHash);
            Assert.True(await db.UserRoles.AnyAsync(role => role.UserId == human.Id && role.RoleId == admin.Id));

            await migrator.MigrateAsync("20261011184624_AddOwnedTeamInvitations");
            db.ChangeTracker.Clear();
            Assert.Equal(taskCount, await db.Tasks.CountAsync());
            Assert.Equal(projectCount, await db.Projects.CountAsync());
            Assert.Equal(userCount + 1, await db.Users.CountAsync());
            Assert.All(await db.Teams.AsNoTracking().ToListAsync(), team => Assert.Null(team.OwnerId));
            Assert.Empty(await db.Set<DAL.Entities.TeamInvitation>().ToListAsync());

            await migrator.MigrateAsync("20261011193014_AddTaskBrowserVersion");
            db.ChangeTracker.Clear();
            var versionedTasks = await db.Tasks.AsNoTracking().ToListAsync();
            Assert.Equal(taskCount, versionedTasks.Count);
            Assert.All(versionedTasks, task => Assert.NotEqual(Guid.Empty, task.Version));
            Assert.Equal(taskCount, versionedTasks.Select(task => task.Version).Distinct().Count());
        }
        finally
        {
            if (owned)
            {
                // Guard teardown even though the name is created from a local GUID.
                if (!name.StartsWith("taskmanager_migration_test_", StringComparison.Ordinal) || name.Length != 59)
                    throw new InvalidOperationException("Refusing cleanup of a database outside the test namespace.");
                SqlConnection.ClearAllPools();
                await using var cleanupConnection = new SqlConnection(server.ConnectionString);
                await cleanupConnection.OpenAsync();
                await using var drop = new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", cleanupConnection);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TASKMANAGER_TEST_SQL_SERVER")))
            Skip = "Real SQL Server not configured. Set TASKMANAGER_TEST_SQL_SERVER to an isolated test server; no SQLite substitution.";
    }
}
