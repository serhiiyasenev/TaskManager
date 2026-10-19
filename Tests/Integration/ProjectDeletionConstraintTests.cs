using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Integration;

public sealed class ProjectDeletionConstraintTests
{
    [Theory]
    [InlineData(101, false)]
    [InlineData(105, true)]
    public async Task DeletingNonemptyProject_ReturnsConflictAndPreservesProjectAndTasks(int actor, bool admin)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(actor, admin);
        var response = await client.DeleteAsync("/api/projects/101");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.True(await db.Projects.AnyAsync(project => project.Id == 101));
            Assert.Equal(new[] { 101, 103 }, await db.Tasks.Where(task => task.ProjectId == 101).OrderBy(task => task.Id).Select(task => task.Id).ToArrayAsync());
        });
    }

    [Fact]
    public async Task EmptyProject_CanBeDeletedByItsAuthorWithoutRemovingOtherData()
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(102);
        var response = await client.DeleteAsync("/api/projects/103");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await factory.WithDatabaseAsync(async db =>
        {
            Assert.False(await db.Projects.AnyAsync(project => project.Id == 103));
            Assert.Equal(4, await db.Tasks.CountAsync());
        });
    }

    [Theory]
    [InlineData(102, 101, HttpStatusCode.Forbidden)]
    [InlineData(103, 101, HttpStatusCode.NotFound)]
    public async Task ConstraintDoesNotExposeOrDeleteAProjectOutsideCallerRights(int actor, int project, HttpStatusCode status)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(actor);
        Assert.Equal(status, (await client.DeleteAsync($"/api/projects/{project}")).StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.Equal(4, await db.Projects.CountAsync()));
    }
}
