using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Tests.Integration;

public class ProductionModelSecurityTests
{
    [Fact]
    public void RuntimeModel_DoesNotSeedAccountsOrBusinessData()
    {
        using var db = new TaskContext(new DbContextOptionsBuilder<TaskContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=True;TrustServerCertificate=True")
            .Options);
        var model = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;

        // HasData belongs to migrations. Production must not acquire demo
        // credentials, privileges or business data from its runtime model.
        foreach (var entityType in model.GetEntityTypes())
        {
            Assert.Empty(entityType.GetSeedData());
        }
    }
}
