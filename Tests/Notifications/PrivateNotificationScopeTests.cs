extern alias NotifierLib;
using DAL.Context;
using DAL.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NotifierLib::Notifier.Notifications;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Notifications;

public sealed class PrivateNotificationScopeTests
{
    [Fact]
    public async Task StableCurrentScopeIsPrivateAndChangesWithTeamStampAndRoles()
    {
        await using var db = Database();
        var user = User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var scope = new DatabaseNotificationRecipientScope(db, TimeProvider.System);
        var original = await scope.GetGroupAsync(user.Id);
        Assert.NotNull(original);
        Assert.DoesNotContain(user.SecurityStamp!, original);
        Assert.Equal(original, await scope.GetGroupAsync(user.Id));
        user.TeamId = 2;
        await db.SaveChangesAsync();
        var changedTeam = await scope.GetGroupAsync(user.Id);
        Assert.NotEqual(original, changedTeam);
        user.SecurityStamp = "new-stamp";
        await db.SaveChangesAsync();
        var changedStamp = await scope.GetGroupAsync(user.Id);
        Assert.NotEqual(changedTeam, changedStamp);
        db.Roles.Add(new IdentityRole<int> { Id = 1, Name = "admin", NormalizedName = "ADMIN" });
        db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = 1 });
        await db.SaveChangesAsync();
        Assert.NotEqual(changedStamp, await scope.GetGroupAsync(user.Id));
    }

    [Theory]
    [InlineData("unconfirmed")]
    [InlineData("locked")]
    [InlineData("missing-stamp")]
    [InlineData("deleted")]
    public async Task RevokedRecipientCannotReceiveNewPrivateHints(string condition)
    {
        await using var db = Database();
        var user = User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var scope = new DatabaseNotificationRecipientScope(db, TimeProvider.System);
        Assert.NotNull(await scope.GetGroupAsync(user.Id));
        if (condition == "unconfirmed") user.EmailConfirmed = false;
        if (condition == "locked") user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(20);
        if (condition == "missing-stamp") user.SecurityStamp = null;
        if (condition == "deleted") db.Users.Remove(user);
        await db.SaveChangesAsync();
        Assert.Null(await scope.GetGroupAsync(user.Id));
        Assert.Null(await scope.GetGroupAsync(0));
    }

    [Fact]
    public async Task UsersWithEqualTeamAndStampHaveDifferentGroups()
    {
        await using var db = Database();
        db.Users.AddRange(User(), new User("other", "Other", "Member", "other@example.test")
            { Id = 8, TeamId = 1, EmailConfirmed = true, SecurityStamp = "synthetic-stamp" });
        await db.SaveChangesAsync();
        var scope = new DatabaseNotificationRecipientScope(db, TimeProvider.System);
        Assert.NotEqual(await scope.GetGroupAsync(7), await scope.GetGroupAsync(8));
    }

    private static TaskContext Database() => new(new DbContextOptionsBuilder<TaskContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static User User() => new("recipient", "Synthetic", "Member", "recipient@example.test")
        { Id = 7, TeamId = 1, EmailConfirmed = true, SecurityStamp = "synthetic-stamp" };
}
