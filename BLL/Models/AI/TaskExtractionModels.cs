#nullable enable

namespace BLL.Models.AI;

public sealed record CreateAiTaskDraftRequest(
    string NotesText,
    int ProjectId,
    DateOnly? NoteDate,
    string TimeZoneId,
    string Locale = "uk");

public sealed record AiTaskDraftDto(
    Guid Id,
    int OwnerId,
    int ProjectId,
    Guid Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Locale,
    string TimeZoneId,
    DateOnly ReferenceDate,
    IReadOnlyList<AiTaskDraftItemDto> Items);

public sealed record AiTaskDraftItemDto(
    Guid DraftItemId,
    string Title,
    string Description,
    int? ProposedAssigneeId,
    string? OriginalAssigneeText,
    DateTimeOffset? ProposedDueAtUtc,
    string? OriginalDueText,
    string SourceQuote,
    int SourceStart,
    int SourceLength,
    bool IsSuggestion,
    IReadOnlyList<string> Interpretations,
    IReadOnlyList<string> ClarificationReasons,
    int? MaterializedTaskId);

public sealed record AiTaskCommitRequest(
    Guid? ExpectedVersion,
    IReadOnlyList<AiTaskCommitItem> Items);

public sealed record AiTaskCommitItem(
    Guid DraftItemId,
    string Title,
    string Description,
    int AssigneeId,
    DateTimeOffset? DueAtUtc,
    bool ReminderEnabled = false,
    int? ReminderOffsetMinutes = null,
    bool EscalationEnabled = false,
    int? EscalationDelayMinutes = null);

public sealed record AiTaskCommitResponse(
    Guid DraftId,
    Guid DraftVersion,
    IReadOnlyList<AiTaskCommitResultItem> Items);

public sealed record AiTaskCommitResultItem(
    Guid DraftItemId,
    int TaskId,
    Guid TaskVersion,
    Guid OutboxMessageId);

public sealed record AiProviderStatusDto(
    bool Available,
    string Provider,
    string? ReasonCode,
    int MaxInputCharacters,
    int MaxItems);

// Provider data is internal to the extraction boundary. Raw output is never an
// authorized write request and must pass server validation before persistence.
public sealed record AllowedAiAssignee(int Id, string DisplayName);

public sealed record TaskExtractionProviderInput(
    string NotesText,
    int ProjectId,
    DateOnly ReferenceDate,
    string TimeZoneId,
    string Locale,
    IReadOnlyList<AllowedAiAssignee> AllowedAssignees);

public sealed record TaskExtractionProviderResult(
    string RawJson,
    bool Refused = false,
    bool Truncated = false,
    int? InputTokens = null,
    int? OutputTokens = null,
    string? Model = null);

public sealed record AiValidatedTaskCandidate(
    string Title,
    string Description,
    int? ProposedAssigneeId,
    string? OriginalAssigneeText,
    DateTimeOffset? ProposedDueAtUtc,
    string? OriginalDueText,
    string SourceQuote,
    int SourceStart,
    int SourceLength,
    bool IsSuggestion,
    IReadOnlyList<string> Interpretations,
    IReadOnlyList<string> ClarificationReasons);
