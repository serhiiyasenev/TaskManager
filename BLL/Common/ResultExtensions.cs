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
            "Error.NotFound" => new NotFoundObjectResult(new { error = result.Error.Message }),
            var code when code == "Error.Validation" || code.StartsWith("Error.Validation.") => 
                new BadRequestObjectResult(new { error = result.Error.Message }),
            "Error.Conflict" => new ConflictObjectResult(new { error = result.Error.Message }),
            "Error.Unauthorized" => new UnauthorizedObjectResult(new { error = result.Error.Message }),
            "Error.Forbidden" => new ObjectResult(new { error = result.Error.Message }) { StatusCode = 403 },
            _ => new ObjectResult(new { error = result.Error.Message }) { StatusCode = 500 }
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
            "Error.NotFound" => new NotFoundObjectResult(new { error = result.Error.Message }),
            var code when code == "Error.Validation" || code.StartsWith("Error.Validation.") => 
                new BadRequestObjectResult(new { error = result.Error.Message }),
            "Error.Conflict" => new ConflictObjectResult(new { error = result.Error.Message }),
            "Error.Unauthorized" => new UnauthorizedObjectResult(new { error = result.Error.Message }),
            "Error.Forbidden" => new ObjectResult(new { error = result.Error.Message }) { StatusCode = 403 },
            _ => new ObjectResult(new { error = result.Error.Message }) { StatusCode = 500 }
        };
    }

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
