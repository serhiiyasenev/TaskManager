#nullable enable
using BLL.Models.AI;

namespace BLL.Services.AI;

public sealed record TaskExtractionGoldTask(string SourceQuote, int? AssigneeId, DateTimeOffset? DueAtUtc);
public sealed record TaskExtractionEvaluationScore(int TruePositives, int FalsePositives, int FalseNegatives,
    int CorrectAssignees, int CorrectDeadlines, int UnauthorizedAssigneeCount, double Precision, double Recall);
public sealed record TaskExtractionEvaluationCase(bool StrictSchemaValid, bool GroundedOutputValid,
    TaskExtractionEvaluationScore? RawScore, TaskExtractionEvaluationScore? ValidatedScore, string? FailureCode);

public static class TaskExtractionEvaluation
{
    public static TaskExtractionEvaluationScore Score(IReadOnlyList<TaskExtractionGoldTask> gold,
        IReadOnlyList<AiValidatedTaskCandidate> candidates, IReadOnlyList<AllowedAiAssignee> allowed)
        => throw new NotImplementedException();
    public static TaskExtractionEvaluationCase Evaluate(TaskExtractionProviderInput input, TaskExtractionProviderResult result,
        IReadOnlyList<TaskExtractionGoldTask> gold, int maxItems = 25) => throw new NotImplementedException();
}
