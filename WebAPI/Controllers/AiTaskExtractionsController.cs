using BLL.Common;
using BLL.Interfaces;
using BLL.Models.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/ai")]
public sealed class AiTaskExtractionsController(IAiTaskDraftService drafts) : ControllerBase
{
    public const string RatePolicy = "ai-extraction";

    [HttpGet("status")]
    public ActionResult<AiProviderStatusDto> Status() => Ok(drafts.GetStatus());

    [HttpPost("task-extractions")]
    [EnableRateLimiting(RatePolicy)]
    [RequestSizeLimit(131_072)]
    public async Task<ActionResult<AiTaskDraftDto>> Extract(CreateAiTaskDraftRequest request, CancellationToken ct)
    {
        var result = await drafts.ExtractAsync(request, ct);
        return result.IsFailure ? result.ToActionResult()
            : CreatedAtAction(nameof(Get), new { draftId = result.Value!.Id }, result.Value);
    }

    [HttpGet("task-extractions/{draftId:guid}")]
    public async Task<ActionResult<AiTaskDraftDto>> Get(Guid draftId, CancellationToken ct)
        => (await drafts.GetAsync(draftId, ct)).ToActionResult();

    [HttpPost("task-extractions/{draftId:guid}/commit")]
    [RequestSizeLimit(131_072)]
    public async Task<ActionResult<AiTaskCommitResponse>> Commit(Guid draftId, AiTaskCommitRequest request, CancellationToken ct)
    {
        var keys = Request.Headers["Idempotency-Key"];
        if (keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0]) || keys[0]!.Length > 128
            || keys[0]!.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.' or ':')))
            return Result<AiTaskCommitResponse>.Failure(Error.Validation("IdempotencyKey", "Supply one Idempotency-Key of 1–128 letters, digits, hyphens, underscores, dots or colons.")).ToActionResult();
        return (await drafts.CommitAsync(draftId, request, keys[0]!, ct)).ToActionResult();
    }

    [HttpDelete("task-extractions/{draftId:guid}")]
    public async Task<IActionResult> Delete(Guid draftId, CancellationToken ct)
    {
        var result = await drafts.DeleteAsync(draftId, ct);
        return result.IsFailure ? result.ToActionResult() : NoContent();
    }
}
