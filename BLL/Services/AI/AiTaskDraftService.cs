#nullable enable
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BLL.Authorization;
using BLL.Common;
using BLL.Configuration;
using BLL.Interfaces;
using BLL.Models.AI;
using BLL.Models.Messaging;
using DAL.Context;
using DAL.Entities;
using DAL.Enum;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskEntity = DAL.Entities.Task;

namespace BLL.Services.AI;

public sealed class AiTaskDraftService(
    TaskContext db,
    IUserAccess access,
    ITaskExtractionProvider provider,
    IOptions<TaskExtractionOptions> aiOptions,
    IOptions<ReminderOptions> reminderOptions,
    IOptions<RabbitMqOptions> rabbitOptions,
    TimeProvider clock,
    ILogger<AiTaskDraftService> logger) : IAiTaskDraftService
{
    private readonly TaskExtractionOptions _options = aiOptions.Value;
    private readonly ReminderOptions _reminders = reminderOptions.Value;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaximumReminderMinutes = 7 * 24 * 60;
    private const int CleanupBatchSize = 100;
    private static readonly Meter Meter = new("TaskManager.AI", "1.0.0");
    private static readonly Counter<long> ExtractionCount = Meter.CreateCounter<long>("ai.extractions");
    private static readonly Counter<long> FailureCount = Meter.CreateCounter<long>("ai.failures");
    private static readonly Counter<long> TokenReservations = Meter.CreateCounter<long>("ai.reserved_tokens");
    private static readonly Counter<long> UsedTokens = Meter.CreateCounter<long>("ai.used_tokens");
    private static readonly Counter<long> AcceptedItems = Meter.CreateCounter<long>("ai.accepted_items");
    private static readonly Counter<long> EditedItems = Meter.CreateCounter<long>("ai.edited_items");
    private static readonly Histogram<double> InferenceLatency = Meter.CreateHistogram<double>("ai.inference_duration", "ms");

    public AiProviderStatusDto GetStatus() => new(
        provider.IsAvailable, provider.ProviderName, provider.UnavailableReasonCode,
        _options.MaxInputCharacters, _options.MaxItems);

    public async Task<Result<AiTaskDraftDto>> ExtractAsync(CreateAiTaskDraftRequest request, CancellationToken ct = default)
    {
        var inputError = ValidateInput(request);
        if (inputError != Error.None) return inputError;
        var initial = await ReadProjectAccessAsync(request.ProjectId, hideForeign: true, ct);
        if (initial.IsFailure) return initial.Error;
        if (!provider.IsAvailable) return Unavailable();

        var allowed = await ReadAllowedAssigneesAsync(initial.Value!.TeamId, ct);
        if (allowed.IsFailure) return allowed.Error;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId);
        var reference = request.NoteDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var input = new TaskExtractionProviderInput(request.NotesText, request.ProjectId, reference,
            request.TimeZoneId, request.Locale, allowed.Value!);

        int reservation;
        try { reservation = provider.CalculateReservationTokens(input); }
        catch (TaskExtractionProviderException exception) { return ProviderError(exception); }
        if (reservation <= 0) return Unavailable();
        var reserved = await ReserveBudgetAsync(request.ProjectId, initial.Value!.UserId, reservation, ct);
        if (reserved.IsFailure) return reserved.Error;
        TokenReservations.Add(reservation);

        TaskExtractionProviderResult output;
        var inferenceStart = Stopwatch.GetTimestamp();
        try { output = await provider.ExtractAsync(input, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TaskExtractionProviderException exception) { FailureCount.Add(1); return ProviderError(exception); }
        catch (OperationCanceledException) { FailureCount.Add(1); return Unavailable(); }
        catch (Exception exception)
        {
            // Provider exception content can contain request bodies. Record only
            // a fixed failure category, never the exception, notes or task title.
            logger.LogWarning("AI extraction failed ({FailureType}).", exception.GetType().Name);
            FailureCount.Add(1);
            return Unavailable();
        }
        finally { InferenceLatency.Record(Stopwatch.GetElapsedTime(inferenceStart).TotalMilliseconds); }
        if (output.InputTokens is > 0 && output.OutputTokens is >= 0)
            UsedTokens.Add((long)output.InputTokens.Value + output.OutputTokens.Value);
        var validated = TaskExtractionOutputValidator.Validate(input, output, _options.MaxItems);
        if (validated.IsFailure)
        {
            FailureCount.Add(1);
            return Error.Custom("Error.ServiceUnavailable", "The AI response could not be safely validated. Try again or create tasks manually.");
        }

        var persisted = await InTransactionAsync<AiTaskDraftDto>(async () =>
        {
            var current = await ReadProjectAccessAsync(request.ProjectId, hideForeign: false, ct);
            if (current.IsFailure) return Result<AiTaskDraftDto>.Failure(current.Error);
            if (current.Value!.UserId != initial.Value!.UserId) return Error.Forbidden;
            var directory = await ReadAllowedAssigneesAsync(current.Value.TeamId, ct);
            if (directory.IsFailure) return directory.Error;
            var refreshed = TaskExtractionOutputValidator.Validate(input with { AllowedAssignees = directory.Value! }, output, _options.MaxItems);
            if (refreshed.IsFailure) return Unavailable();
            var candidates = validated.Value!.Select((candidate, index) => RefreshProposal(candidate, refreshed.Value![index])).ToArray();
            var now = clock.GetUtcNow().UtcDateTime;
            var draft = new AiTaskDraft
            {
                Id = Guid.NewGuid(), OwnerId = current.Value.UserId, ProjectId = request.ProjectId, Version = Guid.NewGuid(),
                NotesText = request.NotesText, Locale = request.Locale, TimeZoneId = request.TimeZoneId, ReferenceDate = reference,
                CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(_options.DraftLifetimeHours),
                RetainUntilUtc = now.AddHours(_options.DraftLifetimeHours).AddDays(_options.ReceiptLifetimeDays),
                Items = candidates.Select((candidate, ordinal) => new AiTaskDraftItem
                {
                    Id = Guid.NewGuid(), Ordinal = ordinal, CandidateJson = JsonSerializer.Serialize(candidate, Json)
                }).ToList()
            };
            db.Set<AiTaskDraft>().Add(draft);
            await db.SaveChangesAsync(ct);
            var final = await ReadProjectAccessAsync(request.ProjectId, hideForeign: false, ct);
            if (final.IsFailure || final.Value!.UserId != draft.OwnerId)
                return Result<AiTaskDraftDto>.Failure(final.IsFailure ? final.Error : Error.Forbidden);
            var finalDirectory = await ReadAllowedAssigneesAsync(final.Value.TeamId, ct);
            if (finalDirectory.IsFailure) return finalDirectory.Error;
            var finalCandidates = TaskExtractionOutputValidator.Validate(input with { AllowedAssignees = finalDirectory.Value! }, output, _options.MaxItems);
            if (finalCandidates.IsFailure || candidates.Where((candidate, index) => candidate.ProposedAssigneeId.HasValue
                    && candidate.ProposedAssigneeId != finalCandidates.Value![index].ProposedAssigneeId).Any())
                return Error.Conflict("The project's assignee directory changed. Review the notes again.");
            return Result<AiTaskDraftDto>.Success(ToDto(draft));
        }, ct);
        if (persisted.IsSuccess) ExtractionCount.Add(1);
        else FailureCount.Add(1);
        return persisted;
    }

    public async Task<Result<AiTaskDraftDto>> GetAsync(Guid draftId, CancellationToken ct = default)
    {
        var caller = await access.GetScopeAsync(ct);
        if (caller is null) return Error.Unauthorized;
        var cleanup = await RedactExpiredOwnedDraftAsync(draftId, caller.UserId, ct);
        if (cleanup.IsFailure) return cleanup.Error;
        var draft = await db.Set<AiTaskDraft>().AsNoTracking().Include(value => value.Items)
            .ThenInclude(value => value.Materialization)
            .SingleOrDefaultAsync(value => value.Id == draftId && value.OwnerId == caller.UserId, ct);
        if (draft is null) return Error.NotFound("AI draft", draftId.ToString("D"));
        var project = await ReadProjectAccessAsync(draft.ProjectId, hideForeign: false, ct);
        if (project.IsFailure) return project.Error;
        if (draft.DeletedAtUtc.HasValue || draft.ExpiresAtUtc <= clock.GetUtcNow().UtcDateTime || draft.NotesText is null)
            return Gone();
        return ToDto(draft);
    }

    public async Task<Result<AiTaskCommitResponse>> CommitAsync(Guid draftId, AiTaskCommitRequest request,
        string idempotencyKey, CancellationToken ct = default)
    {
        if (request.ExpectedVersion is null || request.ExpectedVersion == Guid.Empty)
            return Error.Custom("Error.PreconditionRequired", "The expected draft version is required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128 || idempotencyKey.Any(character => character < 33 || character > 126))
            return Error.Validation("IdempotencyKey", "A visible ASCII idempotency key of at most 128 characters is required.");
        if (request.Items is null || request.Items.Count == 0 || request.Items.Count > _options.MaxItems)
            return Error.Validation("Items", "Select at least one item within the configured limit.");
        if (request.Items.Any(value => value is null) || request.Items.Select(value => value.DraftItemId).Distinct().Count() != request.Items.Count)
            return Error.Validation("Items", "Each selected draft item must occur exactly once.");
        foreach (var item in request.Items)
        {
            var error = ValidateCommitItem(item);
            if (error != Error.None) return error;
        }
        var normalized = request.Items.Select(value => value with
        {
            Title = value.Title.Trim(), Description = value.Description.Trim(), DueAtUtc = value.DueAtUtc?.ToUniversalTime()
        }).OrderBy(value => value.DraftItemId).ToArray();
        var keyHash = Hash(idempotencyKey);
        var payloadHash = Hash(JsonSerializer.Serialize(new { draftId, expectedVersion = request.ExpectedVersion.Value, items = normalized }, Json));
        var currentCaller = await access.GetScopeAsync(ct);
        if (currentCaller is null) return Error.Unauthorized;
        // Expiry is a separate committed privacy transition. Returning Gone from
        // the business transaction must not roll private source erasure back.
        var cleanup = await RedactExpiredOwnedDraftAsync(draftId, currentCaller.UserId, ct);
        if (cleanup.IsFailure) return cleanup.Error;

        // A duplicate insert after a competing commit must be recovered through
        // the durable receipt. A failed local transaction leaves no receipt.
        var result = await InTransactionAsync<AiTaskCommitResponse>(() => CommitCoreAsync(draftId, request.ExpectedVersion.Value, normalized,
            keyHash, payloadHash, ct), ct);
        if (result.IsSuccess || result.Error.Code != "Error.Conflict") return result;
        var caller = await access.GetScopeAsync(ct);
        if (caller is null) return Error.Unauthorized;
        var receipt = await db.Set<AiTaskCommitReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OwnerId == caller.UserId && value.KeyHash == keyHash, ct);
        return receipt is null ? result : await ReplayAsync(receipt, draftId, request.ExpectedVersion.Value, payloadHash, ct);
    }

    private async Task<Result<AiTaskCommitResponse>> CommitCoreAsync(Guid draftId, Guid expectedVersion,
        AiTaskCommitItem[] selected, string keyHash, string payloadHash, CancellationToken ct)
    {
        var caller = await access.GetScopeAsync(ct);
        if (caller is null) return Error.Unauthorized;
        var receipt = await db.Set<AiTaskCommitReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OwnerId == caller.UserId && value.KeyHash == keyHash, ct);
        if (receipt is not null) return await ReplayAsync(receipt, draftId, expectedVersion, payloadHash, ct);
        var draft = await db.Set<AiTaskDraft>().Include(value => value.Items).ThenInclude(value => value.Materialization)
            .SingleOrDefaultAsync(value => value.Id == draftId && value.OwnerId == caller.UserId, ct);
        if (draft is null) return Error.NotFound("AI draft", draftId.ToString("D"));
        var project = await ReadProjectAccessAsync(draft.ProjectId, hideForeign: false, ct);
        if (project.IsFailure) return project.Error;
        var now = clock.GetUtcNow().UtcDateTime;
        if (draft.DeletedAtUtc.HasValue || draft.ExpiresAtUtc <= now || draft.NotesText is null) return Gone();
        if (draft.Version != expectedVersion) return Error.Conflict("The draft changed. Reload it before committing another selection.");
        var items = draft.Items.ToDictionary(value => value.Id);
        foreach (var selection in selected)
        {
            if (!items.TryGetValue(selection.DraftItemId, out var item)) return Error.Validation("DraftItemId", "The selected item does not belong to this draft.");
            if (item.Materialization is not null) return Error.Conflict("A selected draft item was already materialized.");
        }
        var assignment = await ValidateAssigneesAsync(project.Value!.TeamId, selected.Select(value => value.AssigneeId), ct);
        if (assignment != Error.None) return assignment;

        var tasks = selected.Select(selection => new TaskEntity
        {
            ProjectId = draft.ProjectId, PerformerId = selection.AssigneeId, Name = selection.Title, Description = selection.Description,
            State = TaskState.ToDo, CreatedAt = now, DueDate = selection.DueAtUtc?.UtcDateTime,
            ReminderEnabled = selection.ReminderEnabled, ReminderOffsetMinutes = selection.ReminderEnabled
                ? selection.ReminderOffsetMinutes ?? Math.Clamp(_reminders.DefaultReminderOffsetMinutes, 1, MaximumReminderMinutes) : null,
            EscalationEnabled = selection.EscalationEnabled, EscalationDelayMinutes = selection.EscalationEnabled
                ? selection.EscalationDelayMinutes ?? Math.Clamp(_reminders.DefaultEscalationDelayMinutes, 1, MaximumReminderMinutes) : null
        }).ToArray();
        var edited = selected.Count(selection => IsEdited(items[selection.DraftItemId], selection));
        db.Tasks.AddRange(tasks);
        await db.SaveChangesAsync(ct);
        var committed = new List<AiTaskCommitResultItem>(tasks.Length);
        for (var index = 0; index < tasks.Length; index++)
        {
            var task = tasks[index];
            var id = Guid.NewGuid();
            db.Set<AiTaskMaterialization>().Add(new AiTaskMaterialization
            {
                DraftItemId = selected[index].DraftItemId, OriginalTaskId = task.Id, LiveTaskId = task.Id,
                OutboxMessageId = id, CreatedAtUtc = now
            });
            var envelope = new TaskNotificationEnvelope
            {
                Type = NotificationEventType.TaskCreated, CorrelationId = id.ToString("N"),
                TaskCreated = new TaskCreatedNotification(id, task.Id, task.Name, task.PerformerId, task.ProjectId, project.Value.Name, now)
            };
            db.NotificationOutbox.Add(new NotificationOutboxMessage
            {
                Id = id, TaskId = task.Id, Kind = NotificationOutboxKind.TaskCreated,
                QueueName = rabbitOptions.Value.ReminderQueueName ?? rabbitOptions.Value.QueueName,
                Payload = JsonSerializer.Serialize(envelope), CreatedAtUtc = now, Status = NotificationOutboxStatus.Pending
            });
            committed.Add(new AiTaskCommitResultItem(selected[index].DraftItemId, task.Id, task.Version, id));
        }
        draft.Version = Guid.NewGuid();
        var response = new AiTaskCommitResponse(draft.Id, draft.Version, committed);
        db.Set<AiTaskCommitReceipt>().Add(new AiTaskCommitReceipt
        {
            Id = Guid.NewGuid(), OwnerId = caller.UserId, SourceDraftId = draft.Id, ProjectId = draft.ProjectId,
            KeyHash = keyHash, PayloadHash = payloadHash, ExpectedDraftVersion = expectedVersion, ResultDraftVersion = draft.Version,
            ResponseJson = JsonSerializer.Serialize(response, Json), CreatedAtUtc = now,
            ReplayExpiresAtUtc = now.AddDays(_options.ReceiptLifetimeDays)
        });
        await db.SaveChangesAsync(ct);
        var final = await ReadProjectAccessAsync(draft.ProjectId, hideForeign: false, ct);
        if (final.IsFailure || final.Value!.UserId != draft.OwnerId) return final.IsFailure ? final.Error : Error.Forbidden;
        var finalAssignment = await ValidateAssigneesAsync(final.Value.TeamId, selected.Select(value => value.AssigneeId), ct);
        if (finalAssignment != Error.None) return Error.Conflict("An assignee's membership changed. Refresh the draft and retry.");
        // Metrics are emitted after this transaction commits by its coordinator;
        // no tags contain note content, names or actor/resource identifiers.
        _committedMetrics = (selected.Length, edited);
        return response;
    }

    private async Task<Result<AiTaskCommitResponse>> ReplayAsync(AiTaskCommitReceipt receipt, Guid draftId,
        Guid expectedVersion, string payloadHash, CancellationToken ct)
    {
        var rights = await ReadProjectAccessAsync(receipt.ProjectId, hideForeign: false, ct);
        if (rights.IsFailure) return rights.Error;
        if (rights.Value!.UserId != receipt.OwnerId) return Error.NotFound("AI draft", draftId.ToString("D"));
        if (receipt.ReplayExpiresAtUtc <= clock.GetUtcNow().UtcDateTime) return Gone();
        if (receipt.SourceDraftId != draftId || receipt.ExpectedDraftVersion != expectedVersion || receipt.PayloadHash != payloadHash)
            return Error.Conflict("This idempotency key was already used with another confirmed payload.");
        return JsonSerializer.Deserialize<AiTaskCommitResponse>(receipt.ResponseJson, Json)
            ?? throw new InvalidOperationException("Stored AI receipt is invalid.");
    }

    public async Task<Result> DeleteAsync(Guid draftId, CancellationToken ct = default)
    {
        var result = await InTransactionAsync<bool>(async () =>
        {
            var caller = await access.GetScopeAsync(ct);
            if (caller is null) return Result<bool>.Failure(Error.Unauthorized);
            var draft = await db.Set<AiTaskDraft>().Include(value => value.Items)
                .SingleOrDefaultAsync(value => value.Id == draftId && value.OwnerId == caller.UserId, ct);
            if (draft is null) return Error.NotFound("AI draft", draftId.ToString("D"));
            Redact(draft, clock.GetUtcNow().UtcDateTime, _options.ReceiptLifetimeDays);
            await db.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }, ct);
        return result.IsSuccess ? Result.Success() : result.Error;
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
    {
        var result = await InTransactionAsync<int>(async () =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var drafts = await db.Set<AiTaskDraft>().Include(value => value.Items)
                .Where(value => value.RetainUntilUtc <= now || (value.ExpiresAtUtc <= now && value.NotesText != null))
                .OrderBy(value => value.ExpiresAtUtc).Take(CleanupBatchSize).ToListAsync(ct);
            foreach (var draft in drafts)
            {
                if (draft.RetainUntilUtc <= now) db.Set<AiTaskDraft>().Remove(draft);
                else Redact(draft, now, _options.ReceiptLifetimeDays);
            }
            var receipts = await db.Set<AiTaskCommitReceipt>().Where(value => value.ReplayExpiresAtUtc <= now)
                .OrderBy(value => value.ReplayExpiresAtUtc).Take(CleanupBatchSize).ToListAsync(ct);
            db.Set<AiTaskCommitReceipt>().RemoveRange(receipts);
            var cutoff = DateOnly.FromDateTime(now).AddDays(-7);
            var ledger = await db.Set<AiDailyUsage>().Where(value => value.DayUtc < cutoff)
                .OrderBy(value => value.DayUtc).Take(CleanupBatchSize).ToListAsync(ct);
            db.Set<AiDailyUsage>().RemoveRange(ledger);
            await db.SaveChangesAsync(ct);
            return Result<int>.Success(drafts.Count + receipts.Count + ledger.Count);
        }, ct);
        if (result.IsFailure) throw new InvalidOperationException("AI retention cleanup could not complete.");
        return result.Value;
    }

    private async Task<Result<bool>> ReserveBudgetAsync(int projectId, int ownerId, int tokens, CancellationToken ct) =>
        await InTransactionAsync<bool>(async () =>
        {
            var rights = await ReadProjectAccessAsync(projectId, hideForeign: false, ct);
            if (rights.IsFailure) return Result<bool>.Failure(rights.Error);
            if (rights.Value!.UserId != ownerId) return Error.Forbidden;
            var day = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            // Consistent product-then-user lock ordering bounds cross-replica
            // reservations and prevents each replica from owning its own budget.
            foreach (var (key, budget) in new[] { ("product", _options.ProductDailyTokenBudget), ($"user:{ownerId}", _options.UserDailyTokenBudget) })
            {
                var usage = await db.Set<AiDailyUsage>().SingleOrDefaultAsync(value => value.ScopeKey == key && value.DayUtc == day, ct);
                if (usage is null)
                {
                    usage = new AiDailyUsage { ScopeKey = key, DayUtc = day, Version = Guid.NewGuid() };
                    db.Set<AiDailyUsage>().Add(usage);
                }
                if (budget <= 0 || tokens > budget || usage.ReservedTokens > budget - tokens)
                    return Error.Custom("Error.TooManyRequests", "The configured AI daily token budget has been reached.");
                usage.ReservedTokens += tokens;
                usage.Version = Guid.NewGuid();
            }
            await db.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }, ct);

    private async Task<Result<ProjectAccess>> ReadProjectAccessAsync(int projectId, bool hideForeign, CancellationToken ct)
    {
        var caller = await access.GetScopeAsync(ct);
        if (caller is null) return Error.Unauthorized;
        var project = await db.Projects.AsNoTracking().Where(value => value.Id == projectId)
            .Select(value => new { value.TeamId, value.Name }).SingleOrDefaultAsync(ct);
        if (project is null) return Error.NotFound("Project", projectId);
        if (!caller.IsAdmin && caller.TeamId != project.TeamId)
            return hideForeign ? Error.NotFound("Project", projectId) : Error.Forbidden;
        return new ProjectAccess(caller.UserId, project.TeamId, project.Name);
    }

    private async Task<Error> ValidateAssigneesAsync(int teamId, IEnumerable<int> assignees, CancellationToken ct)
    {
        var ids = assignees.Distinct().ToArray();
        var count = await db.Users.AsNoTracking().CountAsync(user => ids.Contains(user.Id) && user.TeamId == teamId, ct);
        return count == ids.Length ? Error.None : Error.Validation("AssigneeId", "Every assignee must be a current member of the project's team.");
    }

    private async Task<Result<IReadOnlyList<AllowedAiAssignee>>> ReadAllowedAssigneesAsync(int teamId, CancellationToken ct)
    {
        var directory = await db.Users.AsNoTracking().Where(user => user.TeamId == teamId)
            .OrderBy(user => user.Id).Select(user => new AllowedAiAssignee(user.Id, (user.FirstName + " " + user.LastName).Trim()))
            .Take(_options.MaxAllowedAssignees + 1).ToListAsync(ct);
        return directory.Count > _options.MaxAllowedAssignees
            ? Error.Validation("ProjectId", "The project's assignee directory exceeds the configured AI input limit.")
            : Result<IReadOnlyList<AllowedAiAssignee>>.Success(directory);
    }

    private static AiValidatedTaskCandidate RefreshProposal(AiValidatedTaskCandidate original, AiValidatedTaskCandidate current)
    {
        if (original.ProposedAssigneeId == current.ProposedAssigneeId) return original;
        // A newly joined person who shares a name is never substituted for the
        // person identified at inference time. Unresolved proposals also require
        // an explicit human choice even if the directory later becomes unique.
        return original with
        {
            ProposedAssigneeId = null,
            Interpretations = original.Interpretations.Where(value => value != "assignee_name_resolution").ToArray(),
            ClarificationReasons = original.ClarificationReasons.Concat(current.ClarificationReasons)
                .Append("assignee_membership_changed").Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private Error ValidateInput(CreateAiTaskDraftRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NotesText) || request.NotesText.Length > _options.MaxInputCharacters)
            return Error.Validation("NotesText", "Notes are required within the configured character limit.");
        if (request.ProjectId <= 0) return Error.Validation("ProjectId", "A permitted project is required.");
        if (request.Locale is not ("uk" or "en")) return Error.Validation("Locale", "Use Ukrainian or English.");
        if (string.IsNullOrWhiteSpace(request.TimeZoneId) || request.TimeZoneId.Length > 128)
            return Error.Validation("TimeZoneId", "A supported IANA time zone is required.");
        try
        {
            if (request.TimeZoneId != "UTC" && !TimeZoneInfo.TryConvertIanaIdToWindowsId(request.TimeZoneId, out _))
                return Error.Validation("TimeZoneId", "A supported IANA time zone is required.");
            _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId);
        }
        catch (TimeZoneNotFoundException) { return Error.Validation("TimeZoneId", "A supported IANA time zone is required."); }
        catch (InvalidTimeZoneException) { return Error.Validation("TimeZoneId", "A supported IANA time zone is required."); }
        return Error.None;
    }

    private static Error ValidateCommitItem(AiTaskCommitItem item)
    {
        if (item.DraftItemId == Guid.Empty) return Error.Validation("DraftItemId", "A draft item is required.");
        if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 100)
            return Error.Validation("Title", "A title of at most 100 characters is required.");
        if (item.Description is null || item.Description.Length > 1000)
            return Error.Validation("Description", "Description must contain at most 1000 characters.");
        if (item.AssigneeId <= 0) return Error.Validation("AssigneeId", "Choose an allowed assignee.");
        if ((item.ReminderEnabled || item.EscalationEnabled) && item.DueAtUtc is null)
            return Error.Validation("DueAtUtc", "A due date is required for notifications.");
        if (item.ReminderOffsetMinutes is < 1 or > MaximumReminderMinutes || item.EscalationDelayMinutes is < 1 or > MaximumReminderMinutes)
            return Error.Validation("Reminder", "Notification offsets must be between one minute and seven days.");
        return Error.None;
    }

    private static AiTaskDraftDto ToDto(AiTaskDraft draft) => new(draft.Id, draft.OwnerId, draft.ProjectId, draft.Version,
        Utc(draft.CreatedAtUtc), Utc(draft.ExpiresAtUtc), draft.Locale, draft.TimeZoneId, draft.ReferenceDate,
        draft.Items.OrderBy(value => value.Ordinal).Select(value =>
        {
            var candidate = JsonSerializer.Deserialize<AiValidatedTaskCandidate>(value.CandidateJson!, Json)
                ?? throw new InvalidOperationException("Stored AI candidate is invalid.");
            return new AiTaskDraftItemDto(value.Id, candidate.Title, candidate.Description, candidate.ProposedAssigneeId,
                candidate.OriginalAssigneeText, candidate.ProposedDueAtUtc, candidate.OriginalDueText,
                candidate.SourceQuote, candidate.SourceStart, candidate.SourceLength, candidate.IsSuggestion,
                candidate.Interpretations, candidate.ClarificationReasons, value.Materialization?.OriginalTaskId);
        }).ToArray());

    private async Task<Result<bool>> RedactExpiredOwnedDraftAsync(Guid draftId, int ownerId, CancellationToken ct) =>
        await InTransactionAsync<bool>(async () =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var draft = await db.Set<AiTaskDraft>().Include(value => value.Items)
                .SingleOrDefaultAsync(value => value.Id == draftId && value.OwnerId == ownerId
                    && value.ExpiresAtUtc <= now && value.NotesText != null, ct);
            if (draft is null) return Result<bool>.Success(false);
            Redact(draft, now, _options.ReceiptLifetimeDays);
            await db.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }, ct);

    private static void Redact(AiTaskDraft draft, DateTime now, int retentionDays)
    {
        draft.NotesText = null;
        foreach (var item in draft.Items) item.CandidateJson = null;
        draft.DeletedAtUtc ??= now;
        var retentionEnd = now.AddDays(retentionDays);
        if (draft.RetainUntilUtc > retentionEnd) draft.RetainUntilUtc = retentionEnd;
        draft.Version = Guid.NewGuid();
    }

    private static bool IsEdited(AiTaskDraftItem item, AiTaskCommitItem selection)
    {
        var candidate = JsonSerializer.Deserialize<AiValidatedTaskCandidate>(item.CandidateJson!, Json)!;
        return candidate.Title.Trim() != selection.Title || candidate.Description.Trim() != selection.Description
            || candidate.ProposedAssigneeId != selection.AssigneeId || candidate.ProposedDueAtUtc?.ToUniversalTime() != selection.DueAtUtc;
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static Error Gone() => Error.Custom("Error.Gone", "The draft or commit receipt has expired or was erased.");
    private static Error Unavailable() => Error.Custom("Error.ServiceUnavailable", "AI is unavailable. Create tasks manually or retry later.");
    private static Error ProviderError(TaskExtractionProviderException exception) => exception.SafeCode switch
    {
        "Ai.InputLimit" => Error.Validation("NotesText", "The AI input exceeds a configured input limit."),
        "Ai.RateLimited" => Error.Custom("Error.TooManyRequests", "The AI provider is rate limited. Retry later."),
        _ => Unavailable()
    };

    private async Task<Result<T>> InTransactionAsync<T>(Func<Task<Result<T>>> operation, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return Error.Conflict("The AI operation requires its own short transaction.");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            _committedMetrics = null;
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var result = await operation();
                if (result.IsFailure)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    db.ChangeTracker.Clear();
                    return result;
                }
                await transaction.CommitAsync(ct);
                if (_committedMetrics is { } metrics)
                {
                    AcceptedItems.Add(metrics.Accepted);
                    EditedItems.Add(metrics.Edited);
                }
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception) when (IsDeadlock(exception))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                logger.LogWarning("AI database operation encountered a deadlock (attempt {Attempt}).", attempt + 1);
                if (attempt == 1) return Error.Conflict("The AI operation raced with another write. Retry using the same idempotency key.");
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                return Error.Conflict("The AI operation conflicted with another write. Retry using the same idempotency key.");
            }
            catch (Exception exception)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                logger.LogError("AI database operation failed ({FailureType}).", exception.GetType().Name);
                return Error.UnexpectedError;
            }
        }
        return Error.Conflict("The AI operation raced with another write.");
    }

    private static bool IsDeadlock(Exception exception) => exception is SqlException { Number: 1205 }
        or DbUpdateException { InnerException: SqlException { Number: 1205 } };
    private sealed record ProjectAccess(int UserId, int TeamId, string Name);
    private (int Accepted, int Edited)? _committedMetrics;
}
