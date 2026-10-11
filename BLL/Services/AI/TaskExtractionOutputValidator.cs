#nullable enable
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BLL.Common;
using BLL.Models.AI;

namespace BLL.Services.AI;

public static class TaskExtractionOutputValidator
{
    public static Result<IReadOnlyList<AiValidatedTaskCandidate>> ParseSchema(TaskExtractionProviderResult result, int maxItems = 25)
    {
        if (result.Refused) return Failure("Ai.Refused");
        if (result.Truncated) return Failure("Ai.Truncated");
        if (string.IsNullOrWhiteSpace(result.RawJson) || result.RawJson.Length > 256_000 || maxItems is < 1 or > 25)
            return Failure("Ai.InvalidOutput");
        try
        {
            using var document = JsonDocument.Parse(result.RawJson, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            RequireFields(root, ["items"]);
            var items = root.GetProperty("items");
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > maxItems) return Failure("Ai.InvalidOutput");
            return Result<IReadOnlyList<AiValidatedTaskCandidate>>.Success(items.EnumerateArray().Select(ReadCandidate).ToArray());
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException
            or KeyNotFoundException or OverflowException or ArgumentException or RegexMatchTimeoutException)
        { return Failure("Ai.InvalidOutput"); }
    }

    private static readonly string[] Fields = ["title", "description", "proposedAssigneeId", "originalAssigneeText",
        "proposedDueAtUtc", "originalDueText", "sourceQuote", "sourceStart", "sourceLength", "isSuggestion",
        "interpretations", "clarificationReasons"];

    public static Result<IReadOnlyList<AiValidatedTaskCandidate>> Validate(
        TaskExtractionProviderInput input, TaskExtractionProviderResult result, int maxItems = 25)
    {
        var parsed = ParseSchema(result, maxItems);
        if (parsed.IsFailure) return parsed;
        try
        {
            var candidates = new List<AiValidatedTaskCandidate>();
            foreach (var item in parsed.Value!)
            {
                var title = item.Title;
                var description = item.Description;
                var originalAssignee = item.OriginalAssigneeText;
                var originalDue = item.OriginalDueText;
                var quote = item.SourceQuote;
                var start = item.SourceStart;
                var length = item.SourceLength;
                var suggestion = item.IsSuggestion;
                if (start < 0 || length <= 0 || start > input.NotesText.Length || length > input.NotesText.Length - start
                    || length != quote.Length || !input.NotesText.AsSpan(start, length).SequenceEqual(quote.AsSpan())
                    || !IsCompleteUtf16Boundary(input.NotesText, start) || !IsCompleteUtf16Boundary(input.NotesText, start + length))
                    return Failure("Ai.InvalidSource");
                if ((originalAssignee is not null && !quote.Contains(originalAssignee, StringComparison.Ordinal))
                    || (originalDue is not null && !quote.Contains(originalDue, StringComparison.Ordinal)))
                    return Failure("Ai.InvalidSource");

                var interpretations = new List<string>();
                var clarifications = new List<string>();
                if (!quote.Contains(title, StringComparison.Ordinal)) interpretations.Add("title_interpretation");
                if (description.Length > 0 && !quote.Contains(description, StringComparison.Ordinal)) interpretations.Add("description_interpretation");
                var assigneeId = ResolveAssignee(input.AllowedAssignees, originalAssignee, clarifications);
                if (HasAlternativeAssignees(quote, originalAssignee))
                {
                    assigneeId = null;
                    clarifications.RemoveAll(code => code is "assignee_missing" or "assignee_unknown");
                    if (!clarifications.Contains("assignee_ambiguous")) clarifications.Add("assignee_ambiguous");
                }
                if (assigneeId is not null) interpretations.Add("assignee_name_resolution");
                var due = HasAlternativeDates(quote) ? (Utc: (DateTimeOffset?)null, DateOnly: false)
                    : TaskExtractionDateResolver.Resolve(originalDue, input.ReferenceDate, input.TimeZoneId);
                if (due.Utc is not null)
                {
                    interpretations.Add("deadline_text_resolution");
                    if (due.DateOnly) interpretations.Add("date_only_end_of_day");
                }
                else clarifications.Add(string.IsNullOrWhiteSpace(originalDue) ? "due_date_missing" : "due_date_unresolved");
                if (!HasExplicitCommitment(quote))
                { suggestion = true; clarifications.Add("commitment_unresolved"); }
                candidates.Add(new(title, description, assigneeId, originalAssignee, due.Utc, originalDue,
                    quote, start, length, suggestion, interpretations, clarifications));
            }
            return Result<IReadOnlyList<AiValidatedTaskCandidate>>.Success(candidates);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException
            or KeyNotFoundException or OverflowException or ArgumentException or RegexMatchTimeoutException)
        { return Failure("Ai.InvalidOutput"); }
    }

    private static AiValidatedTaskCandidate ReadCandidate(JsonElement item)
    {
        RequireFields(item, Fields);
        var modelId = item.GetProperty("proposedAssigneeId");
        int? assigneeId = modelId.ValueKind == JsonValueKind.Null ? null : modelId.GetInt32();
        var modelDue = ReadString(item, "proposedDueAtUtc", 50, nullable: true);
        DateTimeOffset? due = null;
        if (modelDue is not null)
        {
            if (!Regex.IsMatch(modelDue, @"T.*(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
                || !DateTimeOffset.TryParse(modelDue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) throw new JsonException();
            due = parsed;
        }
        return new(ReadString(item, "title", 100, allowEmpty: false)!, ReadString(item, "description", 1000)!, assigneeId,
            ReadString(item, "originalAssigneeText", 201, nullable: true), due, ReadString(item, "originalDueText", 100, nullable: true),
            ReadString(item, "sourceQuote", 20_000, allowEmpty: false)!, item.GetProperty("sourceStart").GetInt32(),
            item.GetProperty("sourceLength").GetInt32(), item.GetProperty("isSuggestion").GetBoolean(),
            ReadLabels(item.GetProperty("interpretations")), ReadLabels(item.GetProperty("clarificationReasons")));
    }

    private static Result<IReadOnlyList<AiValidatedTaskCandidate>> Failure(string code)
        => Error.Custom(code, "The AI output could not be validated. Please edit or retry the notes.");

    private static void RequireFields(JsonElement element, string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException();
        var properties = element.EnumerateObject().ToArray();
        if (properties.Length != fields.Length || properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length
            || properties.Any(p => !fields.Contains(p.Name, StringComparer.Ordinal))) throw new JsonException();
    }

    private static string? ReadString(JsonElement item, string name, int maximum, bool nullable = false, bool allowEmpty = true)
    {
        var element = item.GetProperty(name);
        if (nullable && element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.String) throw new JsonException();
        var value = element.GetString()!;
        if (value.Length > maximum || (!allowEmpty && string.IsNullOrWhiteSpace(value))) throw new JsonException();
        return value;
    }

    private static IReadOnlyList<string> ReadLabels(JsonElement labels)
    {
        if (labels.ValueKind != JsonValueKind.Array || labels.GetArrayLength() > 16) throw new JsonException();
        foreach (var label in labels.EnumerateArray())
            if (label.ValueKind != JsonValueKind.String || label.GetString()!.Length > 100) throw new JsonException();
        // Model-authored labels are not trusted explanations. The server generates
        // the small fixed set of factual resolution/interpretation markers above.
        return labels.EnumerateArray().Select(label => label.GetString()!).ToArray();
    }

    private static bool IsCompleteUtf16Boundary(string text, int offset)
        => offset <= 0 || offset >= text.Length || !char.IsHighSurrogate(text[offset - 1]) || !char.IsLowSurrogate(text[offset]);

    private static bool HasAlternativeAssignees(string source, string? original)
    {
        if (string.IsNullOrWhiteSpace(original)) return false;
        var name = Regex.Escape(original.Trim());
        return Regex.IsMatch(source, $@"(?:{name}\s+(?:or|and|або|чи|і|та)\s+\p{{L}}|\p{{L}}+\s+(?:or|and|або|чи|і|та)\s+{name})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static bool HasAlternativeDates(string source)
    {
        var dates = Regex.Matches(source,
            @"\b(?:today|tomorrow|monday|tuesday|wednesday|thursday|friday|saturday|sunday|сьогодні|завтра|післязавтра|понеділок|понеділка|вівторок|вівторка|середа|середи|середу|четвер|четверга|п'ятниця|п'ятниці|п'ятницю|субота|суботи|суботу|неділя|неділі|неділю)\b|\d{4}-\d{2}-\d{2}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        for (var index = 1; index < dates.Count; index++)
        {
            var previous = dates[index - 1]; var current = dates[index];
            var separator = source.Substring(previous.Index + previous.Length, current.Index - previous.Index - previous.Length);
            if (!previous.Value.Equals(current.Value, StringComparison.OrdinalIgnoreCase)
                && Regex.IsMatch(separator, @"\b(?:or|або|чи|instead|ні)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return true;
        }
        return false;
    }

    private static int? ResolveAssignee(IReadOnlyList<AllowedAiAssignee> allowed, string? source, List<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(source)) { reasons.Add("assignee_missing"); return null; }
        var normalized = source.Trim();
        var full = allowed.Where(a => a.DisplayName.Trim().Equals(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
        var matches = full.Length > 0 ? full : allowed.Where(a => a.DisplayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]
            .Equals(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 1) return matches[0].Id;
        reasons.Add(matches.Length > 1 ? "assignee_ambiguous" : "assignee_unknown");
        return null;
    }

    private static bool HasExplicitCommitment(string source)
    {
        var negative = @"не домовилися|ще не погод|не узгод|можливо|варто|has not been agreed|not agreed|nobody has agreed|maybe|might|could|completed|was done";
        if (Regex.IsMatch(source, negative, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return false;
        return Regex.IsMatch(source,
            @"\b(will|shall|must|agreed to|commits? to|task:|action:|todo:)\b|перевірить|підготує|оновить|надішле|створить|зробить|виправить|проведе|узгодить|додасть|видалить|завершить|розробить|реалізує|перевіримо|підготуємо|оновимо|беру на себе|повинен|зобов'яз",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}
