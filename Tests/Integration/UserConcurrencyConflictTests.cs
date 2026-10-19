using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Tests.Integration;

// Actual HTTP, EF Identity concurrency predicate and SQLite rollback. The
// same-transaction command fault is not a claim about concurrent SQL locks.
public sealed class UserConcurrencyConflictTests
{
    [Fact]
    public async Task ProfileUpdate_StampChangedAtUpdateReturnsConflictAndPreservesProfile()
    {
        var fault = new StampAtUpdate();
        using var factory = await DataIsolationFactory.CreateAsync(fault);
        using var client = factory.CreateUserClient(101);
        var before = await SafeSnapshotAsync(factory);
        var originalStamp = await ReadStampAsync(factory);
        fault.Arm();

        var response = await client.PutAsJsonAsync("/api/users/101", new
        {
            teamId = 101, email = "member101@example.test", userName = "member101",
            firstName = "Synthetic edit must not commit", lastName = "Same current scope",
            birthDay = "1995-01-01"
        });

        Assert.True(fault.Injected, "The valid self-profile update must reach its native UPDATE boundary.");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await SafeSnapshotAsync(factory));
        // Boolean diagnostics keep both original and private injected stamps
        // out of assertion failure output while proving full rollback.
        Assert.True(originalStamp == await ReadStampAsync(factory));
    }

    private static async Task<string?> ReadStampAsync(DataIsolationFactory factory)
    {
        string? value = null;
        await factory.WithDatabaseAsync(async db =>
        {
            value = await db.Users.AsNoTracking().Where(user => user.Id == 101)
                .Select(user => user.ConcurrencyStamp).SingleAsync();
        });
        return value;
    }

    private static async Task<string> SafeSnapshotAsync(DataIsolationFactory factory)
    {
        string result = "";
        await factory.WithDatabaseAsync(async db =>
        {
            result = JsonSerializer.Serialize(new
            {
                // Public synthetic profile fields only; no token, password,
                // hash, security stamp or concurrency stamp is serialized.
                users = await db.Users.AsNoTracking().OrderBy(user => user.Id).Select(user => new
                {
                    user.Id, user.TeamId, user.UserName, user.Email, user.FirstName,
                    user.LastName, user.BirthDay, user.RegisteredAt, user.EmailConfirmed
                }).ToListAsync(),
                projects = await db.Projects.CountAsync(), tasks = await db.Tasks.CountAsync(),
                teams = await db.Teams.CountAsync(), roles = await db.UserRoles.CountAsync()
            });
        });
        return result;
    }

    private sealed class StampAtUpdate : DbCommandInterceptor
    {
        private bool _armed;
        public bool Injected { get; private set; }
        public void Arm() => _armed = true;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await InjectAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await InjectAsync(command, cancellationToken);
            return result;
        }

        private async Task InjectAsync(DbCommand command, CancellationToken ct)
        {
            if (!_armed || Injected || !command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Split("WHERE", 2, StringSplitOptions.None)[0].Contains("\"AspNetUsers\"", StringComparison.Ordinal)) return;
            Assert.NotNull(command.Transaction);
            await using var change = command.Connection!.CreateCommand();
            change.Transaction = command.Transaction;
            change.CommandText = "UPDATE \"AspNetUsers\" SET \"ConcurrencyStamp\" = @stamp WHERE \"Id\" = @target";
            var stamp = change.CreateParameter(); stamp.ParameterName = "@stamp"; stamp.Value = Guid.NewGuid().ToString("N");
            var target = change.CreateParameter(); target.ParameterName = "@target"; target.Value = 101;
            change.Parameters.Add(stamp); change.Parameters.Add(target);
            Assert.Equal(1, await change.ExecuteNonQueryAsync(ct));
            Injected = true;
        }
    }
}
