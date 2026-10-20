#nullable enable
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;

namespace BLL.Services.Notifications;

internal static class NotificationSqlErrors
{
    internal static bool IsDeadlock(Exception exception) => Contains(exception, 1205);
    internal static bool IsDuplicateKey(Exception exception) => Contains(exception, 2601, 2627);

    internal static async Task RollbackAsync(IDbContextTransaction transaction)
    {
        // SQL Server can complete the underlying transaction when selecting a
        // deadlock victim. There is then no live transaction left to roll back.
        if (transaction.GetDbTransaction().Connection is not null)
            await transaction.RollbackAsync(CancellationToken.None);
    }

    private static bool Contains(Exception exception, params int[] numbers)
    {
        Exception? current = exception;
        for (var depth = 0; current is not null && depth < 16; current = current.InnerException, depth++)
        {
            if (current is not SqlException sql) continue;
            foreach (SqlError error in sql.Errors)
                if (numbers.Contains(error.Number)) return true;
        }
        return false;
    }
}
