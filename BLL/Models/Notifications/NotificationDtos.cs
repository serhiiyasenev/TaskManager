// Generated from contracts/notifications.openapi.json. Run the repository generator.
#nullable enable
namespace BLL.Models.Notifications;

public sealed record NotificationInboxItemDto(
    Guid Id,
    Guid MessageId,
    int Kind,
    int TaskId,
    int ProjectId,
    string TaskName,
    string ProjectName,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset? ReadAtUtc = null);

public sealed record NotificationInboxPageDto(
    IReadOnlyList<NotificationInboxItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int UnreadCount);

public sealed record NotificationHintDto(
    Guid NotificationId);
