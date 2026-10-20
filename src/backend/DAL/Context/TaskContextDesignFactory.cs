using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DAL.Context;

// Tooling must not construct the web host, bootstrap accounts, send email or
// require runtime auth credentials merely to scaffold/build migration bundles.
public sealed class TaskContextDesignFactory : IDesignTimeDbContextFactory<TaskContext>
{
    public TaskContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DbConnection")
            ?? "Server=localhost;Database=TaskManagerDesign;Integrated Security=True;TrustServerCertificate=True";
        return new TaskContext(new DbContextOptionsBuilder<TaskContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(TaskContext).Assembly.GetName().Name))
            .Options);
    }
}
