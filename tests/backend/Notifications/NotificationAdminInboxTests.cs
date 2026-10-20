using System.Net;
using System.Text.Json;
using BLL.Interfaces;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Notifications;

public sealed class NotificationAdminInboxTests
{
    [Theory]
    [InlineData(null, false, HttpStatusCode.Unauthorized)]
    [InlineData(101, false, HttpStatusCode.Forbidden)]
    [InlineData(101, true, HttpStatusCode.Forbidden)]
    [InlineData(105, false, HttpStatusCode.Forbidden)]
    public async Task AllInbox_RequiresAdminClaimAndCurrentDatabaseRole(int? userId, bool adminClaim, HttpStatusCode expected)
    {
        using var factory = await NotificationInboxTests.CreateFactoryAsync();
        await SeedAsync(factory);
        using var client = factory.CreateUserClient(userId, adminClaim);
        var response = await client.GetAsync("/api/notifications/all");
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("Team B private task", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RevokedDatabaseRole_DeniesExistingAdminIdentityImmediately()
    {
        using var factory = await NotificationInboxTests.CreateFactoryAsync();
        await SeedAsync(factory);
        using var client = factory.CreateUserClient(105, true);
        Assert.Equal(4, (await ReadAsync(client)).GetProperty("totalCount").GetInt32());
        await factory.WithDatabaseAsync(async db =>
        {
            db.UserRoles.RemoveRange(await db.UserRoles.Where(value => value.UserId == 105).ToListAsync());
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/notifications/all")).StatusCode);
    }

    [Fact]
    public async Task AllInbox_UsesCurrentNamesAcrossTeamsAndRecipientStatusWithoutMutatingReadMarkers()
    {
        using var factory = await NotificationInboxTests.CreateFactoryAsync();
        var ids = await SeedAsync(factory);
        using var client = factory.CreateUserClient(105, true);
        var page = await ReadAsync(client);
        Assert.Equal(4, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page.GetProperty("unreadCount").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, items.Length);
        Assert.Contains(items, value => value.GetProperty("recipientId").GetInt32() == 101
            && value.GetProperty("recipientName").GetString() == "Member 101");
        var foreign = Assert.Single(items, value => value.GetProperty("id").GetGuid() == ids[2]);
        Assert.Equal("Team B private task", foreign.GetProperty("taskName").GetString());
        Assert.Equal("Team B private project", foreign.GetProperty("projectName").GetString());
        Assert.Equal(103, foreign.GetProperty("recipientId").GetInt32());
        Assert.Equal(foreign.GetProperty("id").GetGuid(), foreign.GetProperty("messageId").GetGuid());
        Assert.Equal(JsonValueKind.String, foreign.GetProperty("readAtUtc").ValueKind);
        Assert.Equal(TimeSpan.Zero, foreign.GetProperty("occurredAtUtc").GetDateTimeOffset().Offset);
        Assert.DoesNotContain(items, value => value.GetProperty("id").GetGuid() == ids[4]);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Equal(1, await db.UserNotifications.CountAsync(value => value.ReadAtUtc != null));
            Assert.Null((await db.UserNotifications.SingleAsync(value => value.Id == ids[0])).ReadAtUtc);
        });
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync($"/api/notifications/{ids[0]}/read", null)).StatusCode);
        var personal = await client.GetAsync("/api/notifications");
        personal.EnsureSuccessStatusCode();
        Assert.Equal(0, JsonDocument.Parse(await personal.Content.ReadAsStringAsync()).RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task AllInbox_UnreadFilterKeepsGlobalCountsAndStableTieOrderingAcrossPages()
    {
        using var factory = await NotificationInboxTests.CreateFactoryAsync();
        var ids = await SeedAsync(factory);
        using var client = factory.CreateUserClient(105, true);
        var first = await ReadAsync(client, "?pageSize=1&unreadOnly=true");
        var second = await ReadAsync(client, "?page=2&pageSize=1&unreadOnly=true");
        Assert.Equal(4, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, first.GetProperty("unreadCount").GetInt32());
        Assert.Single(first.GetProperty("items").EnumerateArray());
        Assert.Equal(ids[0], first.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(ids[1], second.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(2, second.GetProperty("page").GetInt32());
        var empty = await ReadAsync(client, "?page=100000&pageSize=100&unreadOnly=true");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal(4, empty.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("reassigned")]
    [InlineData("recipient-left")]
    [InlineData("project-moved")]
    [InlineData("task-moved")]
    public async Task AllInbox_RetainsHistoryAfterReassignmentButLinksCurrentTaskProject(string change)
    {
        using var factory = await NotificationInboxTests.CreateFactoryAsync();
        var ids = await SeedAsync(factory);
        await factory.WithDatabaseAsync(async db =>
        {
            if (change == "reassigned") (await db.Tasks.SingleAsync(value => value.Id == 101)).PerformerId = 103;
            if (change == "recipient-left") (await db.Users.SingleAsync(value => value.Id == 101)).TeamId = 102;
            if (change == "project-moved") (await db.Projects.SingleAsync(value => value.Id == 101)).TeamId = 102;
            if (change == "task-moved") (await db.Tasks.SingleAsync(value => value.Id == 101)).ProjectId = 102;
            await db.SaveChangesAsync();
        });
        using var admin = factory.CreateUserClient(105, true);
        var page = await ReadAsync(admin);
        var item = Assert.Single(page.GetProperty("items").EnumerateArray(), value => value.GetProperty("id").GetGuid() == ids[0]);
        Assert.Equal(101, item.GetProperty("recipientId").GetInt32());
        Assert.Equal(change == "task-moved" ? 102 : 101, item.GetProperty("projectId").GetInt32());
        Assert.Equal(change == "task-moved" ? "Team B private project" : "Team A project", item.GetProperty("projectName").GetString());
        using var owner = factory.CreateUserClient(101);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsync($"/api/notifications/{ids[0]}/read", null)).StatusCode);
    }

    [Theory]
    [InlineData("task")]
    [InlineData("project")]
    [InlineData("recipient")]
    public async Task AllInbox_ExcludesDeletedOrOrphanedEntities(string missing)
    {
        using var factory = await NotificationInboxTests.CreateFactoryAsync();
        var ids = await SeedAsync(factory);
        await factory.WithDatabaseAsync(async db =>
        {
            if (missing == "task") db.Tasks.Remove(await db.Tasks.SingleAsync(value => value.Id == 101));
            if (missing == "project")
            {
                db.Tasks.RemoveRange(await db.Tasks.Where(value => value.ProjectId == 101).ToListAsync());
                await db.SaveChangesAsync();
                db.Projects.Remove(await db.Projects.SingleAsync(value => value.Id == 101));
            }
            if (missing == "recipient")
            {
                foreach (var task in await db.Tasks.Where(value => value.PerformerId == 101).ToListAsync()) task.PerformerId = 103;
                foreach (var project in await db.Projects.Where(value => value.AuthorId == 101).ToListAsync()) project.AuthorId = 103;
                await db.SaveChangesAsync();
                db.Users.Remove(await db.Users.SingleAsync(value => value.Id == 101));
            }
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateUserClient(105, true);
        var page = await ReadAsync(client);
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain(page.GetProperty("items").EnumerateArray(), value => value.GetProperty("id").GetGuid() == ids[0]);
    }

    [Theory]
    [InlineData("", "", "member101", "member101")]
    [InlineData(" ", " ", null, "101")]
    [InlineData(" Kate ", " ", "member101", "Kate")]
    public async Task AllInbox_RecipientDisplayNameHasSafeFallback(string first, string last, string? username, string expected)
    {
        using var factory = await NotificationInboxTests.CreateFactoryAsync();
        var ids = await SeedAsync(factory);
        await factory.WithDatabaseAsync(async db =>
        {
            var user = await db.Users.SingleAsync(value => value.Id == 101);
            user.FirstName = first; user.LastName = last; user.UserName = username;
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateUserClient(105, true);
        var page = await ReadAsync(client);
        Assert.Equal(expected, page.GetProperty("items").EnumerateArray().Single(value => value.GetProperty("id").GetGuid() == ids[0]).GetProperty("recipientName").GetString());
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=100001")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task AllInbox_ValidatesPaging(string query)
    {
        using var factory = await NotificationInboxTests.CreateFactoryAsync();
        using var client = factory.CreateUserClient(105, true);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/notifications/all" + query)).StatusCode);
    }

    [Fact]
    public async Task AllInbox_ServiceItselfRejectsMissingCaller()
    {
        using var factory = await NotificationInboxTests.CreateFactoryAsync();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<INotificationInboxService>();
        var result = await service.GetAllPageAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal("Error.Unauthorized", result.Error.Code);
    }

    private static async Task<Guid[]> SeedAsync(DataIsolationFactory factory)
    {
        var ids = Enumerable.Range(1, 5).Select(value => Guid.Parse($"00000000-0000-4000-8000-{value:D12}")).ToArray();
        var now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        await factory.WithDatabaseAsync(async db =>
        {
            for (var index = 0; index < ids.Length; index++)
            {
                db.NotificationProcessingMarkers.Add(new NotificationProcessingMarker { MessageId = ids[index],
                    CanonicalPayloadHash = new string('A', 64), ProcessedAtUtc = now, Disposition = NotificationProcessingDisposition.Delivered });
                db.UserNotifications.Add(new UserNotification { Id = ids[index], OwnerId = index is 2 or 3 ? 103 : 101,
                    TaskId = index == 4 ? 999 : index is 2 or 3 ? 102 : 101, ProjectId = index is 2 or 3 ? 102 : 101,
                    Kind = 1, TaskName = "Stale private title", ProjectName = "Stale private project",
                    OccurredAtUtc = index < 2 ? now : now.AddMinutes(-index), ReadAtUtc = index == 2 ? now.AddSeconds(1) : null });
            }
            await db.SaveChangesAsync();
        });
        return ids;
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/notifications/all" + query);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
