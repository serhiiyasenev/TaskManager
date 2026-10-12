using Microsoft.Data.SqlClient;

namespace TaskManager.SqlBootstrap;

public static class Program
{
    public static async System.Threading.Tasks.Task<int> Main()
    {
        try
        {
            var users = SqlBootstrapPlan.ParseUsers(Environment.GetEnvironmentVariable("SqlBootstrap__UsersJson") ?? "");
            var phase = Environment.GetEnvironmentVariable("SqlBootstrap__Phase");
            if (phase is not ("Prepare" or "Finalize")) throw new InvalidOperationException("Explicit bootstrap phase required.");
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DbConnection") ?? "";
            SqlBootstrapConnectionPolicy.Validate(connectionString, users);
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await using var connection = new SqlConnection(connectionString); await connection.OpenAsync(deadline.Token);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(deadline.Token);
            foreach (var user in users)
            {
                await using var command = new SqlCommand(SqlBootstrapPlan.CreatePrincipalSql(user) + "\n"
                    + SqlBootstrapPlan.GrantSql(user, phase == "Finalize"), connection, transaction) { CommandTimeout = 30 };
                await command.ExecuteNonQueryAsync(deadline.Token);
            }
            foreach (var user in users)
            {
                await using var verify = new SqlCommand($"EXECUTE AS USER = '{user.Name}';\n"
                    + SqlBootstrapPlan.ExcessAuthoritySql(user) + "\nREVERT;", connection, transaction) { CommandTimeout = 30 };
                if (Convert.ToInt32(await verify.ExecuteScalarAsync(deadline.Token)) != 0)
                    throw new InvalidOperationException("Runtime SQL authority exceeds the allowed workload role.");
            }
            await transaction.CommitAsync(deadline.Token);
            Console.WriteLine($"TaskManager SQL bootstrap {phase} completed for three workload identities.");
            return 0;
        }
        catch (Exception)
        {
            // SQL exception messages can contain identifiers and provider data.
            // The operator inspects protected SQL diagnostics; release logs stay safe.
            Console.Error.WriteLine("TaskManager SQL bootstrap failed; no success or principal rewrite is assumed.");
            return 1;
        }
    }
}
