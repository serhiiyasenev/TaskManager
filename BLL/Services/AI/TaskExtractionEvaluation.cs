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
    {
        var actionable = candidates.Where(candidate => !candidate.IsSuggestion).ToArray();
        var matched = new HashSet<int>();
        var truePositives = 0;
        var correctAssignees = 0;
        var correctDeadlines = 0;
        var unauthorized = candidates.Count(candidate => candidate.ProposedAssigneeId is int id && !allowed.Any(user => user.Id == id));
        foreach (var candidate in actionable)
        {
            var match = -1;
            for (var index = 0; index < gold.Count; index++)
                if (!matched.Contains(index) && Normalize(gold[index].SourceQuote) == Normalize(candidate.SourceQuote))
                { match = index; break; }
            if (match < 0) continue;
            matched.Add(match);
            truePositives++;
            if (gold[match].AssigneeId == candidate.ProposedAssigneeId) correctAssignees++;
            if (gold[match].DueAtUtc == candidate.ProposedDueAtUtc) correctDeadlines++;
        }
        var falsePositives = actionable.Length - truePositives;
        var falseNegatives = gold.Count - truePositives;
        return new(truePositives, falsePositives, falseNegatives, correctAssignees, correctDeadlines, unauthorized,
            actionable.Length == 0 ? (gold.Count == 0 ? 1 : 0) : (double)truePositives / actionable.Length,
            gold.Count == 0 ? 1 : (double)truePositives / gold.Count);
    }
    public static TaskExtractionEvaluationCase Evaluate(TaskExtractionProviderInput input, TaskExtractionProviderResult result,
        IReadOnlyList<TaskExtractionGoldTask> gold, int maxItems = 25)
    {
        var schema = TaskExtractionOutputValidator.ParseSchema(result, maxItems);
        if (schema.IsFailure) return new(false, false, null, null, schema.Error.Code);
        var raw = Score(gold, schema.Value!, input.AllowedAssignees);
        var grounded = TaskExtractionOutputValidator.Validate(input, result, maxItems);
        if (grounded.IsFailure) return new(true, false, raw, null, grounded.Error.Code);
        return new(true, true, raw, Score(gold, grounded.Value!, input.AllowedAssignees), null);
    }
    private static string Normalize(string quote) => quote.Trim().TrimEnd('.', '!', '?', '。');
}
