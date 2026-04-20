using BLL.Common;
using BLL.Exceptions;
using BLL.Models.Tasks;
using BLL.Models.Teams;
using BLL.Models.Users;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Tests.Unit;

public class ResultExtensionsAndModelsTests
{
    [Fact]
    public void ResultExtensions_ToActionResultT_MapsAllErrorTypes()
    {
        Assert.IsType<NotFoundObjectResult>(Result<string>.Failure(Error.NotFound("Entity", 1)).ToActionResult().Result);
        Assert.IsType<BadRequestObjectResult>(Result<string>.Failure(Error.Validation("Name", "Invalid")).ToActionResult().Result);
        Assert.IsType<ConflictObjectResult>(Result<string>.Failure(Error.Conflict("Conflict")).ToActionResult().Result);
        Assert.IsType<UnauthorizedObjectResult>(Result<string>.Failure(Error.Unauthorized).ToActionResult().Result);

        var forbidden = Assert.IsType<ObjectResult>(Result<string>.Failure(Error.Forbidden).ToActionResult().Result);
        Assert.Equal(403, forbidden.StatusCode);

        var unknown = Assert.IsType<ObjectResult>(Result<string>.Failure(Error.Custom("Error.Custom", "x")).ToActionResult().Result);
        Assert.Equal(500, unknown.StatusCode);
    }

    [Fact]
    public void ResultExtensions_ToActionResult_MapsAllErrorTypes()
    {
        Assert.IsType<NotFoundObjectResult>(Result.Failure(Error.NotFound("Entity", 1)).ToActionResult());
        Assert.IsType<BadRequestObjectResult>(Result.Failure(Error.Validation("Name", "Invalid")).ToActionResult());
        Assert.IsType<ConflictObjectResult>(Result.Failure(Error.Conflict("Conflict")).ToActionResult());
        Assert.IsType<UnauthorizedObjectResult>(Result.Failure(Error.Unauthorized).ToActionResult());

        var forbidden = Assert.IsType<ObjectResult>(Result.Failure(Error.Forbidden).ToActionResult());
        Assert.Equal(403, forbidden.StatusCode);

        var unknown = Assert.IsType<ObjectResult>(Result.Failure(Error.Custom("Error.Custom", "x")).ToActionResult());
        Assert.Equal(500, unknown.StatusCode);
    }

    [Fact]
    public void ResultExtensions_Match_ReturnsExpectedBranch()
    {
        var successValue = Result<int>.Success(5).Match(v => v * 2, _ => -1);
        var failureValue = Result<int>.Failure(Error.UnexpectedError).Match(v => v * 2, _ => -1);
        var successNoValue = Result.Success().Match(() => "ok", _ => "fail");
        var failureNoValue = Result.Failure(Error.UnexpectedError).Match(() => "ok", _ => "fail");

        Assert.Equal(10, successValue);
        Assert.Equal(-1, failureValue);
        Assert.Equal("ok", successNoValue);
        Assert.Equal("fail", failureNoValue);
    }

    [Fact]
    public void TaskDto_ToString_HandlesNullAndNonNullFinishedAt()
    {
        var withFinished = new TaskDto(1, "Task", "Desc", "Done", DateTime.UnixEpoch, DateTime.UnixEpoch.AddDays(1), DateTime.UnixEpoch.AddDays(2), true, 60, true, 120, null, null);
        var withoutFinished = new TaskDto(2, "Task2", "Desc2", "New", DateTime.UnixEpoch, null, null, false, null, false, null, null, null);

        var withText = withFinished.ToString();
        var withoutText = withoutFinished.ToString();

        Assert.Contains("TaskId: 1", withText);
        Assert.DoesNotContain("N/A", withText);
        Assert.Contains("FinishedAt: N/A", withoutText);
    }

    [Fact]
    public void TeamWithMembersDto_And_UserWithTasksDto_ToString_IncludeNestedData()
    {
        var member = new UserDto(1, null, "John", "Doe", "john@example.com", DateTime.UnixEpoch, DateTime.UnixEpoch);
        var team = new TeamWithMembersDto(10, "Team", [member]);
        var task = new TaskDto(1, "Task", "Desc", "New", DateTime.UnixEpoch, null, null, false, null, false, null, null, null);
        var userWithTasks = new UserWithTasksDto(2, "Jane", "Doe", "jane@example.com", DateTime.UnixEpoch, DateTime.UnixEpoch, [task]);

        var teamText = team.ToString();
        var userText = userWithTasks.ToString();

        Assert.Contains("Team Members", teamText);
        Assert.Contains("Tasks:", userText);
        Assert.Contains("TaskId: 1", userText);
    }

    [Fact]
    public void Exceptions_Constructors_WithId_ContainExpectedText()
    {
        var notFound = new NotFoundException("User", 10);
        var canNotDelete = new CanNotDeleteException("User", 10);

        Assert.Contains("id (10)", notFound.Message);
        Assert.Contains("id (10)", canNotDelete.Message);
    }
}
