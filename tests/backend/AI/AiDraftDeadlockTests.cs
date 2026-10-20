using System.Data.Common;
using System.Net;
using System.Reflection;
using DAL.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.AI;

public sealed class AiDraftDeadlockTests
{
    [Fact]
    public async Task WrappedSqlDeadlockAfterWrites_RetriesOnceAndCommitsOneAtomicResult()
    {
        var deadlock = new WrappedSqlFault(1205, failures: 1);
        using var factory = await AiDraftContractTests.CreateFactoryAsync(interceptor: deadlock);
        using var client = factory.CreateUserClient(101);
        var draft = await AiDraftContractTests.CreateDraftAsync(client);
        deadlock.Arm();
        var result = await AiDraftContractTests.CommitAsync(client, draft, [AiDraftContractTests.Item(draft, 0)], "wrapped-deadlock-once");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(1, deadlock.InjectedCount);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Equal(5, await db.Tasks.CountAsync());
            Assert.Single(await db.Set<AiTaskCommitReceipt>().ToListAsync());
            Assert.Single(await db.Set<AiTaskMaterialization>().ToListAsync());
            Assert.Single(await db.NotificationOutbox.ToListAsync());
        });
        var beforeReplay = await AiDraftContractTests.BusinessSnapshotAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await AiDraftContractTests.CommitAsync(client, draft,
            [AiDraftContractTests.Item(draft, 0)], "wrapped-deadlock-once")).StatusCode);
        Assert.Equal(beforeReplay, await AiDraftContractTests.BusinessSnapshotAsync(factory));
    }

    [Fact]
    public async Task RepeatedWrappedSqlDeadlock_StopsAfterTwoAttemptsAndReturns409WithoutSideEffects()
    {
        var deadlock = new WrappedSqlFault(1205, failures: int.MaxValue);
        using var factory = await AiDraftContractTests.CreateFactoryAsync(interceptor: deadlock);
        using var client = factory.CreateUserClient(101);
        var draft = await AiDraftContractTests.CreateDraftAsync(client);
        var before = await AiDraftContractTests.BusinessSnapshotAsync(factory);
        deadlock.Arm();
        await AiDraftContractTests.AssertProblemAsync(await AiDraftContractTests.CommitAsync(client, draft,
            [AiDraftContractTests.Item(draft, 0)], "wrapped-deadlock-exhausted"), HttpStatusCode.Conflict);
        Assert.Equal(2, deadlock.InjectedCount);
        Assert.Equal(before, await AiDraftContractTests.BusinessSnapshotAsync(factory));
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Empty(await db.Set<AiTaskCommitReceipt>().ToListAsync());
            Assert.Empty(await db.Set<AiTaskMaterialization>().ToListAsync());
            Assert.Equal(draft.GetProperty("version").GetGuid(), (await db.Set<AiTaskDraft>().SingleAsync()).Version);
        });
    }

    [Fact]
    public async Task WrappedSqlTimeout_IsNotRetriedAsDeadlock_AndWritesRollBack()
    {
        var timeout = new WrappedSqlFault(-2, failures: int.MaxValue);
        using var factory = await AiDraftContractTests.CreateFactoryAsync(interceptor: timeout);
        using var client = factory.CreateUserClient(101);
        var draft = await AiDraftContractTests.CreateDraftAsync(client);
        var before = await AiDraftContractTests.BusinessSnapshotAsync(factory);
        timeout.Arm();
        await AiDraftContractTests.AssertProblemAsync(await AiDraftContractTests.CommitAsync(client, draft,
            [AiDraftContractTests.Item(draft, 0)], "wrapped-timeout"), HttpStatusCode.InternalServerError);
        Assert.Equal(1, timeout.InjectedCount);
        Assert.Equal(before, await AiDraftContractTests.BusinessSnapshotAsync(factory));
    }

    // This models the documented EF SQL execution-strategy exception boundary
    // after writes. Actual SQL Server concurrency remains a separate CI gate.
    private sealed class WrappedSqlFault(int number, int failures) : DbCommandInterceptor
    {
        private bool _armed;
        private bool _receiptWritten;
        public int InjectedCount { get; private set; }
        public void Arm() => _armed = true;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Inspect(command);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Inspect(command);
            return ValueTask.FromResult(result);
        }
        private void Inspect(DbCommand command)
        {
            if (!_armed) return;
            if (command.CommandText.Contains("INSERT INTO \"AiTaskCommitReceipts\"", StringComparison.Ordinal)) _receiptWritten = true;
            if (!_receiptWritten || InjectedCount >= failures || !command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains("\"AspNetUsers\"", StringComparison.Ordinal)) return;
            _receiptWritten = false;
            InjectedCount++;
            throw new InvalidOperationException("Synthetic EF execution-strategy wrapper.", SqlFailure(number));
        }
    }

    // SqlClient has no public SqlException/SqlError constructor. This test-only
    // descriptor uses the exact pinned 6.1.6 signatures, rather than production
    // helper reflection or obsolete exception serialization.
    private static SqlException SqlFailure(int number)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var errorConstructor = typeof(SqlError).GetConstructor(flags, null,
            [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)], null)!;
        var error = (SqlError)errorConstructor.Invoke([number, (byte)1, (byte)13, "synthetic-server", "Synthetic SQL failure", "", 1, null]);
        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", flags)!.Invoke(collection, [error]);
        var exceptionConstructor = typeof(SqlException).GetConstructor(flags, null,
            [typeof(string), typeof(SqlErrorCollection), typeof(Exception), typeof(Guid)], null)!;
        var exception = (SqlException)exceptionConstructor.Invoke(["Synthetic SQL failure", collection, null, Guid.Empty]);
        Assert.Equal(number, exception.Number);
        return exception;
    }
}
