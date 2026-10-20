using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Integration;

public sealed class TaskReminderBoundsTests
{
    [Theory]
    [InlineData(10081, false)]
    [InlineData(43200, true)]
    public async Task ManualScalarRequest_RejectsOffsetsTheSchedulerWouldClamp(int minutes, bool escalation)
    {
        using var factory = await DataIsolationFactory.CreateAsync();
        using var client = factory.CreateUserClient(101);
        var before = 0;
        await factory.WithDatabaseAsync(async db => before = await db.Tasks.CountAsync());
        var response = await client.PostAsJsonAsync("/api/tasks", new
        {
            projectId = 101, performerId = 101, name = "Check scheduler offset", description = "", state = 0,
            dueDate = DateTime.UtcNow.AddDays(40), reminderEnabled = !escalation,
            reminderOffsetMinutes = escalation ? (int?)null : minutes,
            escalationEnabled = escalation, escalationDelayMinutes = escalation ? minutes : (int?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await factory.WithDatabaseAsync(async db => Assert.Equal(before, await db.Tasks.CountAsync()));
    }
}
