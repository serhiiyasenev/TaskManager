#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BLL.Models.Messaging;

namespace BLL.Services.Notifications;

public sealed record ValidatedNotificationPayload(Guid MessageId, NotificationEventType Type, int TaskId,
    int PerformerId, int ProjectId, TaskNotificationEnvelope Envelope, string CanonicalPayloadHash);

public static class NotificationPayloadValidator
{
    public const int MaxPayloadUtf8Bytes = 65_536;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> RootFields = new(StringComparer.OrdinalIgnoreCase)
        { "Type", "CorrelationId", "ExecutedTask", "Reminder", "TaskCreated" };
    private static readonly HashSet<string> ExecutedFields = new(StringComparer.OrdinalIgnoreCase)
        { "TaskId", "TaskName", "CompletedAtUtc", "PerformerId", "ProjectId", "ProjectName" };
    private static readonly HashSet<string> ReminderFields = new(StringComparer.OrdinalIgnoreCase)
        { "TaskId", "TaskName", "PerformerId", "ProjectId", "ProjectName", "DueDateUtc", "ReminderOffsetMinutes", "EscalationDelayMinutes" };
    private static readonly HashSet<string> CreatedFields = new(StringComparer.OrdinalIgnoreCase)
        { "MessageId", "TaskId", "TaskName", "PerformerId", "ProjectId", "ProjectName", "CreatedAtUtc" };

    public static ValidatedNotificationPayload Parse(Guid messageId, string payload)
    {
        if (messageId == Guid.Empty || string.IsNullOrWhiteSpace(payload) || Encoding.UTF8.GetByteCount(payload) > MaxPayloadUtf8Bytes)
            throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
            ValidateObject(document.RootElement, RootFields);
            if (!document.RootElement.EnumerateObject().Any(value => value.Name.Equals("Type", StringComparison.OrdinalIgnoreCase))) throw Invalid();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Null) continue;
                if (property.Name.Equals("ExecutedTask", StringComparison.OrdinalIgnoreCase)) ValidateObject(property.Value, ExecutedFields);
                if (property.Name.Equals("Reminder", StringComparison.OrdinalIgnoreCase)) ValidateObject(property.Value, ReminderFields);
                if (property.Name.Equals("TaskCreated", StringComparison.OrdinalIgnoreCase)) ValidateObject(property.Value, CreatedFields);
            }
            var envelope = JsonSerializer.Deserialize<TaskNotificationEnvelope>(payload, Json) ?? throw Invalid();
            if (!Enum.IsDefined(envelope.Type) || !Guid.TryParse(envelope.CorrelationId, out var correlation) || correlation != messageId
                || (envelope.ExecutedTask is null ? 0 : 1) + (envelope.Reminder is null ? 0 : 1) + (envelope.TaskCreated is null ? 0 : 1) != 1)
                throw Invalid();
            envelope.CorrelationId = messageId.ToString("N");
            int taskId, performerId, projectId;
            switch (envelope.Type)
            {
                case NotificationEventType.ExecutedTaskCompleted when envelope.ExecutedTask is { } execution:
                    taskId = execution.TaskId; performerId = execution.PerformerId ?? 0; projectId = execution.ProjectId ?? 0;
                    ValidateNames(execution.TaskName, execution.ProjectName);
                    envelope.ExecutedTask = execution with { CompletedAtUtc = Utc(execution.CompletedAtUtc) };
                    break;
                case NotificationEventType.TaskReminder or NotificationEventType.TaskOverdueEscalation when envelope.Reminder is { } reminder:
                    taskId = reminder.TaskId; performerId = reminder.PerformerId; projectId = reminder.ProjectId;
                    ValidateNames(reminder.TaskName, reminder.ProjectName);
                    if (reminder.ReminderOffsetMinutes is < 1 or > 10080 || reminder.EscalationDelayMinutes is < 1 or > 10080) throw Invalid();
                    envelope.Reminder = reminder with { DueDateUtc = Utc(reminder.DueDateUtc) };
                    break;
                case NotificationEventType.TaskCreated when envelope.TaskCreated is { } created:
                    taskId = created.TaskId; performerId = created.PerformerId; projectId = created.ProjectId;
                    if (created.MessageId != messageId) throw Invalid();
                    ValidateNames(created.TaskName, created.ProjectName);
                    envelope.TaskCreated = created with { CreatedAtUtc = Utc(created.CreatedAtUtc) };
                    break;
                default: throw Invalid();
            }
            if (taskId <= 0 || performerId <= 0 || projectId <= 0) throw Invalid();
            // Typed serialization normalizes property order, GUID format, UTC
            // and escaping. Mutable database names never influence this hash.
            var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(envelope, Json)));
            return new(messageId, envelope.Type, taskId, performerId, projectId, envelope, hash);
        }
        catch (JsonException) { throw Invalid(); }
    }

    private static void ValidateObject(JsonElement element, HashSet<string> allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name)) throw Invalid();
    }

    private static void ValidateNames(string? taskName, string? projectName)
    {
        if (string.IsNullOrWhiteSpace(taskName) || taskName.Length > 100 || projectName is { Length: > 100 }) throw Invalid();
    }

    private static DateTime Utc(DateTime value)
    {
        if (value == default) throw Invalid();
        return AsUtc(value);
    }

    internal static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Unspecified
        ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
    private static InvalidDataException Invalid() => new("The notification envelope is invalid.");
}
