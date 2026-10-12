using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using BLL.Interfaces;
using BLL.Models.AI;
using BLL.Services.AI;
using Microsoft.AspNetCore.WebUtilities;

namespace TaskManager.FullStackHost;

internal sealed class FullStackClock : TimeProvider
{
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private long _advanceTicks;
    public override DateTimeOffset GetUtcNow() => _started + GetElapsedTime(0, GetTimestamp()) + TimeSpan.FromTicks(Interlocked.Read(ref _advanceTicks));
    // Timestamp origin must be recorded: the system timestamp is not Unix time.
    private readonly long _timestampOrigin = TimeProvider.System.GetTimestamp();
    public override long GetTimestamp() => TimeProvider.System.GetTimestamp() - _timestampOrigin;
    public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;

    public DateTimeOffset Advance(int minutes)
    {
        if (minutes is < 1 or > 1440) throw new ArgumentOutOfRangeException(nameof(minutes));
        var increase = TimeSpan.FromMinutes(minutes).Ticks;
        while (true)
        {
            var current = Interlocked.Read(ref _advanceTicks);
            if (current + increase > TimeSpan.FromDays(7).Ticks) throw new ArgumentOutOfRangeException(nameof(minutes));
            if (Interlocked.CompareExchange(ref _advanceTicks, current + increase, current) == current) return GetUtcNow();
        }
    }
}

internal sealed record CapturedMail(string? ConfirmationUrl, string? ResetUrl);

internal sealed class FullStackMail(Uri publicOrigin) : IAccountEmailSender
{
    private readonly ConcurrentDictionary<string, CapturedMail> _messages = new(StringComparer.OrdinalIgnoreCase);
    public CapturedMail Get(string email) => _messages.TryGetValue(email, out var value) ? value : new(null, null);
    public Task SendConfirmationAsync(string email, string userId, string token, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var link = QueryHelpers.AddQueryString(new Uri(publicOrigin, "/confirm-email").AbsoluteUri,
            new Dictionary<string, string?> { ["userId"] = userId, ["token"] = Encode(token) });
        _messages.AddOrUpdate(email, new CapturedMail(link, null), (_, old) => old with { ConfirmationUrl = link });
        return Task.CompletedTask;
    }
    public Task SendPasswordResetAsync(string email, string token, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var link = QueryHelpers.AddQueryString(new Uri(publicOrigin, "/reset-password").AbsoluteUri,
            new Dictionary<string, string?> { ["email"] = email, ["token"] = Encode(token) });
        _messages.AddOrUpdate(email, new CapturedMail(null, link), (_, old) => old with { ResetUrl = link });
        return Task.CompletedTask;
    }
    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
}

internal sealed class FullStackAiMode
{
    private string _value = "normal";
    public string Value => Volatile.Read(ref _value);
    public void Set(string value)
    {
        if (value is not ("normal" or "unavailable" or "invalid-output")) throw new ArgumentException("Fixture mode is invalid.");
        Volatile.Write(ref _value, value);
    }
}

// Only the model transport is synthetic. The native provider prompt/schema,
// reservation limits, strict source/date validator and SQL service stay real.
internal sealed class FullStackAiTransport(FullStackAiMode mode) : ITaskExtractionChatTransport
{
    public Task<TaskExtractionProviderResult> CompleteAsync(TaskExtractionChatRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (mode.Value == "unavailable") throw new TaskExtractionProviderException("Ai.Unavailable");
        if (mode.Value == "invalid-output") return Task.FromResult(new TaskExtractionProviderResult("{\"items\":[{\"fabricated\":true}]}"));
        using var input = JsonDocument.Parse(request.UserMessage);
        var notes = input.RootElement.GetProperty("notesText").GetString()!;
        var owner = input.RootElement.GetProperty("allowedAssignees").EnumerateArray()
            .Where(value => value.GetProperty("displayName").GetString() == "Тестовий Власник").ToArray();
        if (owner.Length != 1) throw new TaskExtractionProviderException("Ai.InvalidOutput");
        var sentences = notes.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (sentences.Length != 2 || sentences[0] != "Тестовий Власник завтра підготує міграцію"
            || sentences[1] is not ("Тестовий Власник до п’ятниці перевірить API" or "Тестовий Власник до п'ятниці перевірить API"))
            throw new TaskExtractionProviderException("Ai.InvalidOutput");
        var items = sentences.Select((quote, index) => new
        {
            title = index == 0 ? "Підготувати міграцію" : "Перевірити API",
            description = "",
            proposedAssigneeId = owner[0].GetProperty("id").GetInt32(),
            originalAssigneeText = "Тестовий Власник",
            proposedDueAtUtc = (string?)null,
            originalDueText = index == 0 ? "завтра" : quote.Contains('’') ? "до п’ятниці" : "до п'ятниці",
            sourceQuote = quote,
            sourceStart = notes.IndexOf(quote, StringComparison.Ordinal),
            sourceLength = quote.Length,
            isSuggestion = false,
            interpretations = Array.Empty<string>(),
            clarificationReasons = Array.Empty<string>()
        }).ToArray();
        return Task.FromResult(new TaskExtractionProviderResult(JsonSerializer.Serialize(new { items }),
            InputTokens: 1200, OutputTokens: 300, Model: "explicit-full-stack-fixture"));
    }
}

internal sealed class ModeAwareExtractionProvider(AzureTaskExtractionProvider actual, FullStackAiMode mode) : ITaskExtractionProvider
{
    public bool IsAvailable => mode.Value != "unavailable" && actual.IsAvailable;
    public string ProviderName => actual.ProviderName;
    public string? UnavailableReasonCode => IsAvailable ? null : "Ai.Unavailable";
    public int CalculateReservationTokens(TaskExtractionProviderInput input) => actual.CalculateReservationTokens(input);
    public Task<TaskExtractionProviderResult> ExtractAsync(TaskExtractionProviderInput input, CancellationToken ct = default)
        => actual.ExtractAsync(input, ct);
}
