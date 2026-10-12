using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.AI;

public sealed class AiDraftAuthorizationRaceTests
{
    [Theory]
    [InlineData("departed")]
    [InlineData("replacement-name")]
    [InlineData("new-ambiguity")]
    public async Task AssigneeDirectoryChangesDuringInference_ClearsOriginalProposalWithoutRetargeting(string change)
    {
        var provider = new FixtureExtractionProvider();
        using var factory = await AiDraftContractTests.CreateFactoryAsync(provider);
        provider.BeforeReturn = async _ => await factory.WithDatabaseAsync(async db =>
        {
            if (change != "new-ambiguity") (await db.Users.SingleAsync(value => value.Id == 102)).TeamId = 102;
            if (change != "departed")
            {
                var replacement = await db.Users.SingleAsync(value => value.Id == 104);
                replacement.TeamId = 101; replacement.FirstName = "Сергій"; replacement.LastName = "Інший";
            }
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateUserClient(101);
        var draft = await AiDraftContractTests.CreateDraftAsync(client);
        var changed = draft.GetProperty("items")[1];
        Assert.Equal("Сергій", changed.GetProperty("originalAssigneeText").GetString());
        Assert.Equal(JsonValueKind.Null, AiDraftContractTests.NullableProperty(changed, "proposedAssigneeId").ValueKind);
        Assert.NotEmpty(changed.GetProperty("clarificationReasons").EnumerateArray());
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("assignee")]
    [InlineData("project")]
    [InlineData("admin-role")]
    public async Task RightsChangedAtFinalWrite_RollsBackAllTaskAndAiCommitRecords(string subject)
    {
        var interleaving = new AiCommitInterleaving(subject);
        using var factory = await AiDraftContractTests.CreateFactoryAsync(interceptor: interleaving);
        var admin = subject == "admin-role";
        using var client = factory.CreateUserClient(admin ? 105 : 101, admin);
        JsonElement draft;
        if (admin)
        {
            var response = await client.PostAsJsonAsync("/api/ai/task-extractions", AiDraftContractTests.Extraction(projectId: 102));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            draft = await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        else draft = await AiDraftContractTests.CreateDraftAsync(client);
        var before = await AiDraftContractTests.BusinessSnapshotAsync(factory);
        interleaving.Arm();
        var result = await AiDraftContractTests.CommitAsync(client, draft,
            [AiDraftContractTests.Item(draft, 0, assigneeId: admin ? 103 : 102)], "final-write-rights");
        Assert.True(interleaving.Injected);
        Assert.Contains(result.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Conflict });
        Assert.Equal(before, await AiDraftContractTests.BusinessSnapshotAsync(factory));
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.Empty(await db.Set<AiTaskCommitReceipt>().ToListAsync());
            Assert.Empty(await db.Set<AiTaskMaterialization>().ToListAsync());
            Assert.Equal(draft.GetProperty("version").GetGuid(), (await db.Set<AiTaskDraft>().SingleAsync()).Version);
        });
    }

    // These faults use the same SQLite connection/transaction immediately before
    // the final receipt insert. They prove fresh final authorization guards,
    // rather than concurrent SQL Server locking or outside-transaction revocation.
    private sealed class AiCommitInterleaving(string subject) : DbCommandInterceptor
    {
        private bool _armed;
        public bool Injected { get; private set; }
        public void Arm() => _armed = true;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await InjectAsync(command, cancellationToken);
            return result;
        }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await InjectAsync(command, cancellationToken);
            return result;
        }
        private async Task InjectAsync(DbCommand command, CancellationToken ct)
        {
            if (!_armed || Injected || !command.CommandText.Contains("INSERT INTO \"AiTaskCommitReceipts\"", StringComparison.Ordinal)) return;
            Injected = true;
            await using var mutation = command.Connection!.CreateCommand();
            mutation.Transaction = command.Transaction;
            mutation.CommandText = subject switch
            {
                "admin-role" => "DELETE FROM \"AspNetUserRoles\" WHERE \"UserId\"=105",
                "project" => "UPDATE \"Projects\" SET \"TeamId\"=102 WHERE \"Id\"=101",
                "assignee" => "UPDATE \"AspNetUsers\" SET \"TeamId\"=NULL WHERE \"Id\"=102",
                _ => "UPDATE \"AspNetUsers\" SET \"TeamId\"=NULL WHERE \"Id\"=101"
            };
            Assert.Equal(1, await mutation.ExecuteNonQueryAsync(ct));
        }
    }
}
