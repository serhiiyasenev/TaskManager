using Microsoft.AspNetCore.Mvc;

namespace BLL.Common;

/// <summary>
/// Extension methods for converting Result to ActionResult
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Converts Result<T> to ActionResult<T>
    /// </summary>
    public static ActionResult<T> ToActionResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
        {
            return new OkObjectResult(result.Value);
        }

        return result.Error.Code switch
        {
            "Error.NotFound" => new NotFoundObjectResult(ToProblem(result.Error, 404)),
            var code when code == "Error.Validation" || code.StartsWith("Error.Validation.") => 
                new BadRequestObjectResult(ToProblem(result.Error, 400)),
            "Error.Conflict" => new ConflictObjectResult(ToProblem(result.Error, 409)),
            "Error.Unauthorized" => new UnauthorizedObjectResult(ToProblem(result.Error, 401)),
            "Error.Forbidden" => new ObjectResult(ToProblem(result.Error, 403)) { StatusCode = 403 },
            _ => new ObjectResult(ToProblem(result.Error, 500)) { StatusCode = 500 }
        };
    }

    /// <summary>
    /// Converts Result to ActionResult
    /// </summary>
    public static ActionResult ToActionResult(this Result result)
    {
        if (result.IsSuccess)
        {
            return new OkResult();
        }

        return result.Error.Code switch
        {
            "Error.NotFound" => new NotFoundObjectResult(ToProblem(result.Error, 404)),
            var code when code == "Error.Validation" || code.StartsWith("Error.Validation.") => 
                new BadRequestObjectResult(ToProblem(result.Error, 400)),
            "Error.Conflict" => new ConflictObjectResult(ToProblem(result.Error, 409)),
            "Error.Unauthorized" => new UnauthorizedObjectResult(ToProblem(result.Error, 401)),
            "Error.Forbidden" => new ObjectResult(ToProblem(result.Error, 403)) { StatusCode = 403 },
            _ => new ObjectResult(ToProblem(result.Error, 500)) { StatusCode = 500 }
        };
    }

    private static ProblemDetails ToProblem(Error error, int status) => new()
    {
        Status = status,
        Title = status == 500 ? "internal_error" : error.Code,
        Detail = status == 500 ? "Unexpected server error." : error.Message,
        Extensions = { ["code"] = error.Code }
    };

    /// <summary>
    /// Matches on Result<T> and executes the appropriate function
    /// </summary>
    public static TResult Match<T, TResult>(
        this Result<T> result,
        Func<T, TResult> onSuccess,
        Func<Error, TResult> onFailure)
    {
        return result.IsSuccess ? onSuccess(result.Value!) : onFailure(result.Error);
    }

    /// <summary>
    /// Matches on Result and executes the appropriate function
    /// </summary>
    public static TResult Match<TResult>(
        this Result result,
        Func<TResult> onSuccess,
        Func<Error, TResult> onFailure)
    {
        return result.IsSuccess ? onSuccess() : onFailure(result.Error);
    }
}
