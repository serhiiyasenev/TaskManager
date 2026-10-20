using System.Net;
using System.Text.Json;
using BLL.Interfaces;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.AI;

public sealed class AiDraftRetentionTests
{
    [Theory]
    [InlineData("read")]
    [InlineData("commit")]
    public async Task ExpiredRequest_ErasesPrivateSourceInCommittedCleanupEvenThoughOperationReturns410(string operation)
    {
        var clock = new MutableAiClock();
        using var factory = await AiDraftContractTests.CreateFactoryAsync(clock: clock);
        using var client = factory.CreateUserClient(101);
        var draft = await AiDraftContractTests.CreateDraftAsync(client);
        clock.Advance(TimeSpan.FromHours(25));
        var response = operation == "read"
            ? await client.GetAsync($"/api/ai/task-extractions/{draft.GetProperty("id").GetGuid()}")
            : await AiDraftContractTests.CommitAsync(client, draft, [AiDraftContractTests.Item(draft, 0)], "expired-source");
        await AiDraftContractTests.AssertProblemAsync(response, HttpStatusCode.Gone);
        await factory.WithDatabaseAsync(async db =>
        {
            var stored = await db.Set<AiTaskDraft>().AsNoTracking().Include(value => value.Items).SingleAsync();
            Assert.Null(stored.NotesText);
            Assert.All(stored.Items, value => Assert.Null(value.CandidateJson));
        });
    }

    [Fact]
    public async Task Cleanup_RedactsExpiredNotesThenPurgesOnlyPrivateMetadataAfterReceiptRetention()
    {
        var clock = new MutableAiClock();
        using var factory = await AiDraftContractTests.CreateFactoryAsync(clock: clock);
        using var client = factory.CreateUserClient(101);
        var draft = await AiDraftContractTests.CreateDraftAsync(client);
        var result = await AiDraftContractTests.CommitAsync(client, draft, [AiDraftContractTests.Item(draft, 0)], "retention-receipt");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var before = await AiDraftContractTests.BusinessSnapshotAsync(factory);
        clock.Advance(TimeSpan.FromHours(25));
        using (var scope = factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IAiTaskDraftService>().CleanupExpiredAsync() > 0);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Null((await db.Set<AiTaskDraft>().SingleAsync()).NotesText);
            Assert.All(await db.Set<AiTaskDraftItem>().ToListAsync(), value => Assert.Null(value.CandidateJson));
            Assert.Single(await db.Set<AiTaskCommitReceipt>().ToListAsync());
            Assert.Single(await db.Set<AiTaskMaterialization>().ToListAsync());
            Assert.DoesNotContain(AiDraftContractTests.Notes, (await db.Set<AiTaskCommitReceipt>().SingleAsync()).ResponseJson);
        });
        clock.Advance(TimeSpan.FromDays(8));
        using (var scope = factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IAiTaskDraftService>().CleanupExpiredAsync() > 0);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Empty(await db.Set<AiTaskDraft>().ToListAsync());
            Assert.Empty(await db.Set<AiTaskDraftItem>().ToListAsync());
            Assert.Empty(await db.Set<AiTaskMaterialization>().ToListAsync());
            Assert.Empty(await db.Set<AiTaskCommitReceipt>().ToListAsync());
            Assert.Empty(await db.Set<AiDailyUsage>().ToListAsync());
        });
        Assert.Equal(before, await AiDraftContractTests.BusinessSnapshotAsync(factory));
    }
}
