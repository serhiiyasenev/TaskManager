using System.Text.Json;
using BLL.Models.AI;
using BLL.Services.AI;
using Xunit;

namespace Tests.Unit;

public class TaskExtractionEvaluationTests
{
    private static readonly AllowedAiAssignee[] Allowed = [new(11, "Alice Brown"), new(12, "Bob Smith")];

    [Fact]
    public void MissingAndInventedActions_LowerPrecisionAndRecallIndependentlyOfFieldAccuracy()
    {
        TaskExtractionGoldTask[] gold = [new("Alice will check API.", 11, null), new("Bob will migrate data.", 12, null)];
        AiValidatedTaskCandidate[] actual = [Candidate("Alice will check API.", 11, new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero)), Candidate("Invented action.", null)];
        var score = TaskExtractionEvaluation.Score(gold, actual, Allowed);
        Assert.Equal(1, score.TruePositives); Assert.Equal(1, score.FalsePositives); Assert.Equal(1, score.FalseNegatives);
        Assert.Equal(0.5, score.Precision); Assert.Equal(0.5, score.Recall);
        Assert.Equal(1, score.CorrectAssignees); Assert.Equal(0, score.CorrectDeadlines);
    }

    [Fact]
    public void DuplicatePredictions_DoNotMatchOneGoldTaskTwice()
    {
        var candidate = Candidate("Alice will check API.", 11);
        var score = TaskExtractionEvaluation.Score([new(candidate.SourceQuote, 11, null)], [candidate, candidate], Allowed);
        Assert.Equal(1, score.TruePositives); Assert.Equal(1, score.FalsePositives);
        Assert.Equal(0, score.FalseNegatives); Assert.Equal(0.5, score.Precision);
    }

    [Fact]
    public void Suggestions_AreNotCountedAsActionableObligations()
    {
        var score = TaskExtractionEvaluation.Score([], [Candidate("Maybe improve design.", null) with { IsSuggestion = true }], Allowed);
        Assert.Equal(0, score.TruePositives); Assert.Equal(0, score.FalsePositives);
        Assert.Equal(0, score.FalseNegatives);
    }

    [Fact]
    public void InaccessibleRawAssignee_IsReportedEvenWhenTaskSourceMatches()
    {
        var score = TaskExtractionEvaluation.Score([new("Alice will check API.", 11, null)], [Candidate("Alice will check API.", 999)], Allowed);
        Assert.Equal(1, score.UnauthorizedAssigneeCount); Assert.Equal(0, score.CorrectAssignees);
    }

    [Fact]
    public void SchemaValidity_DoesNotEstablishGroundingOrModelQuality()
    {
        var input = TaskExtractionOutputValidatorTests.Input("Alice will check API.");
        var invented = Candidate("Alice will delete payroll.", 11);
        var result = TaskExtractionEvaluation.Evaluate(input, Output(invented), [new(input.NotesText, 12, null)]);
        Assert.True(result.StrictSchemaValid); Assert.False(result.GroundedOutputValid);
        Assert.NotNull(result.RawScore); Assert.Equal(0, result.RawScore.TruePositives);
        Assert.Null(result.ValidatedScore);
    }

    [Fact]
    public void RawAndValidatedAssigneeAccuracy_AreReportedSeparately()
    {
        var input = TaskExtractionOutputValidatorTests.Input("Alice will check API.");
        var result = TaskExtractionEvaluation.Evaluate(input, Output(Candidate(input.NotesText, 999) with { OriginalAssigneeText = "Alice" }), [new(input.NotesText, 12, null)]);
        Assert.True(result.StrictSchemaValid); Assert.True(result.GroundedOutputValid);
        Assert.Equal(1, result.RawScore!.UnauthorizedAssigneeCount);
        Assert.Equal(0, result.RawScore.CorrectAssignees);
        Assert.Equal(0, result.ValidatedScore!.UnauthorizedAssigneeCount);
        Assert.Equal(1, result.ValidatedScore.CorrectAssignees);
    }

    [Theory]
    [InlineData(true, false, "Ai.Refused")]
    [InlineData(false, true, "Ai.Truncated")]
    public void FailedInference_CannotBecomePerfectEmptyScore(bool refused, bool truncated, string code)
    {
        var result = TaskExtractionEvaluation.Evaluate(TaskExtractionOutputValidatorTests.Input("notes"), new("{\"items\":[]}", refused, truncated), []);
        Assert.False(result.StrictSchemaValid); Assert.False(result.GroundedOutputValid);
        Assert.Null(result.RawScore); Assert.Null(result.ValidatedScore); Assert.Equal(code, result.FailureCode);
    }

    [Fact]
    public void MalformedSchema_IsNotScoredAsAnEmptySuccess()
    {
        var result = TaskExtractionEvaluation.Evaluate(TaskExtractionOutputValidatorTests.Input("notes"), new("{\"items\":[],\"extra\":true}"), []);
        Assert.False(result.StrictSchemaValid); Assert.Null(result.RawScore); Assert.Equal("Ai.InvalidOutput", result.FailureCode);
    }

    [Fact]
    public void CosmeticSentencePunctuation_DoesNotChangeTaskMatch()
    {
        var score = TaskExtractionEvaluation.Score([new("Alice will check API.", 11, null)], [Candidate("Alice will check API", 11)], Allowed);
        Assert.Equal(1, score.TruePositives); Assert.Equal(1.0, score.Precision);
    }

    private static TaskExtractionProviderResult Output(AiValidatedTaskCandidate item)
        => new(JsonSerializer.Serialize(new { items = new[] { item } }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    private static AiValidatedTaskCandidate Candidate(string source, int? assignee, DateTimeOffset? due = null)
        => new("check API", "", assignee, null, due, null, source, 0, source.Length, false, [], []);
}
