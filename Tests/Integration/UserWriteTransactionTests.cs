using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Integration;

public sealed class UserWriteTransactionTests
{
    [Theory]
    [InlineData(101, false, 101, 101)]
    [InlineData(105, true, 103, 102)]
    public async Task ValidProfileWrite_RemainsUsable(int actor, bool admin, int target, int team)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(actor, admin);
        var response = await client.PutAsJsonAsync($"/api/users/{target}", new
        {
            teamId = team, email = $"member{target}@example.test", userName = $"member{target}",
            firstName = "Updated", lastName = "Profile", birthDay = "1995-01-01"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.Equal("Updated",
            (await db.Users.AsNoTracking().SingleAsync(user => user.Id == target)).FirstName));
    }

    [Theory]
    [InlineData(105, true)]
    [InlineData(104, false)]
    public async Task ValidDependencyFreeAccountDeletion_SupportsAdminAndSelf(int actor, bool admin)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(actor, admin);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/users/104")).StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.False(await db.Users.AnyAsync(user => user.Id == 104));
            Assert.Equal(4, await db.Tasks.CountAsync());
            Assert.Equal(4, await db.Projects.CountAsync());
        });
    }
}
