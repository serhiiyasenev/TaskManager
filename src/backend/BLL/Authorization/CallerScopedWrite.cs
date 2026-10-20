#nullable enable
using System.Data;
using BLL.Common;
using DAL.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace BLL.Authorization;

internal static class CallerScopedWrite
{
    internal static async Task<Result<T>> RunAsync<T>(TaskContext? context, IUserAccess? access,
        Func<Task<Result<T>>> action, Func<T, UserAccessScope?, UserAccessScope, bool> allowed,
        ILogger logger, CancellationToken ct)
    {
        // Preserve direct repository callers. HTTP services always resolve both
        // the database context and fresh caller scope through DI.
        if (access is null) return await action();
        if (context is null) return Error.UnexpectedError;
        IDbContextTransaction? owned = null;
        var shared = context.Database.CurrentTransaction;
        try
        {
            if (shared is not null && shared.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                return Error.Conflict("Refresh and retry the update.");
            var transaction = shared ?? (owned = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct));
            var original = await access.GetScopeAsync(ct);
            if (original is null)
            {
                await RollbackAsync(transaction);
                context.ChangeTracker.Clear();
                return Error.Unauthorized;
            }
            var result = await action();
            if (result.IsFailure)
            {
                await RollbackAsync(transaction);
                context.ChangeTracker.Clear();
                return result;
            }
            var current = await access.GetScopeAsync(ct);
            if (!allowed(result.Value!, current, original))
            {
                await RollbackAsync(transaction);
                context.ChangeTracker.Clear();
                return Error.Conflict("Your access changed. Refresh and retry.");
            }
            if (owned is not null) await owned.CommitAsync(ct);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (shared is not null) await RollbackAsync(shared);
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception error) when (IsDeadlock(error) || error is DbUpdateConcurrencyException)
        {
            if (shared is not null) await RollbackAsync(shared);
            context.ChangeTracker.Clear();
            return Error.Conflict("The data changed. Refresh and retry.");
        }
        catch (Exception error)
        {
            if (shared is not null) await RollbackAsync(shared);
            context.ChangeTracker.Clear();
            logger.LogError("A caller-scoped write failed ({FailureType}).", error.GetType().Name);
            return Error.UnexpectedError;
        }
        finally
        {
            if (owned is not null)
            {
                await owned.DisposeAsync();
                context.ChangeTracker.Clear();
            }
        }
    }

    private static async Task RollbackAsync(IDbContextTransaction transaction)
    {
        if (transaction.GetDbTransaction().Connection is not null)
            await transaction.RollbackAsync(CancellationToken.None);
    }

    internal static bool IsDeadlock(Exception exception)
    {
        Exception? current = exception;
        for (var depth = 0; current is not null && depth < 16; current = current.InnerException, depth++)
        {
            if (current is not SqlException sql) continue;
            foreach (SqlError error in sql.Errors)
                if (error.Number == 1205) return true;
        }
        return false;
    }
}
