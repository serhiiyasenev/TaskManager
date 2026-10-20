#nullable enable
using System.Data;
using BLL.Authorization;
using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Notifications;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BLL.Services.Notifications;

public sealed class NotificationInboxService(TaskContext db, IUserAccess access, TimeProvider clock,
    ILogger<NotificationInboxService> logger) : INotificationInboxService
{
    public async Task<Result<NotificationInboxPageDto>> GetPageAsync(int page = 1, int pageSize = 20,
        bool unreadOnly = false, CancellationToken ct = default)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100)
            return Error.Validation("Paging", "Page must be 1–100000 and pageSize must be 1–100.");
        try
        {
            var caller = await access.GetScopeAsync(ct);
            if (caller is null) return Error.Unauthorized;
            var visible = Visible(caller.UserId).AsNoTracking();
            var total = await visible.CountAsync(ct);
            var unread = await visible.CountAsync(value => value.Notification.ReadAtUtc == null, ct);
            var pageQuery = unreadOnly ? visible.Where(value => value.Notification.ReadAtUtc == null) : visible;
            var values = await pageQuery.OrderByDescending(value => value.Notification.OccurredAtUtc)
                .ThenBy(value => value.Notification.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return new NotificationInboxPageDto(values.Select(ToDto).ToArray(), page, pageSize, total, unread);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError("Notification inbox query failed ({FailureType}).", exception.GetType().Name);
            return Error.UnexpectedError;
        }
    }

    public async Task<Result<NotificationInboxItemDto>> MarkReadAsync(Guid notificationId, CancellationToken ct = default)
    {
        if (notificationId == Guid.Empty) return Error.NotFound("Notification", notificationId.ToString("D"));
        if (db.Database.CurrentTransaction is not null) return Error.Conflict("The inbox operation requires its own short transaction.");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var caller = await access.GetScopeAsync(ct);
                if (caller is null) return Error.Unauthorized;
                var row = await Visible(caller.UserId).SingleOrDefaultAsync(value => value.Notification.Id == notificationId, ct);
                if (row is null) return Error.NotFound("Notification", notificationId.ToString("D"));
                row.Notification.ReadAtUtc ??= clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
                var freshCaller = await access.GetScopeAsync(ct);
                var fresh = freshCaller?.UserId == caller.UserId
                    ? await Visible(caller.UserId).AsNoTracking().SingleOrDefaultAsync(value => value.Notification.Id == notificationId, ct) : null;
                if (fresh is null)
                {
                    await NotificationSqlErrors.RollbackAsync(transaction);
                    db.ChangeTracker.Clear();
                    return Error.NotFound("Notification", notificationId.ToString("D"));
                }
                await transaction.CommitAsync(ct);
                return ToDto(fresh);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await NotificationSqlErrors.RollbackAsync(transaction);
                db.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception) when (NotificationSqlErrors.IsDeadlock(exception))
            {
                await NotificationSqlErrors.RollbackAsync(transaction);
                db.ChangeTracker.Clear();
                if (attempt == 1) return Error.Conflict("The inbox operation raced with another write. Retry the same operation.");
            }
            catch (Exception exception)
            {
                await NotificationSqlErrors.RollbackAsync(transaction);
                db.ChangeTracker.Clear();
                logger.LogError("Notification read update failed ({FailureType}).", exception.GetType().Name);
                return Error.UnexpectedError;
            }
        }
        return Error.Conflict("The inbox operation raced with another write.");
    }

    private IQueryable<VisibleNotification> Visible(int userId) =>
        from notification in db.UserNotifications
        join task in db.Tasks on notification.TaskId equals task.Id
        join project in db.Projects on task.ProjectId equals project.Id
        join owner in db.Users on notification.OwnerId equals owner.Id
        where notification.OwnerId == userId && task.PerformerId == userId
            && task.ProjectId == notification.ProjectId && owner.TeamId == project.TeamId
            && task.Name != "" && project.Name != "" && task.Name.Length <= 100 && project.Name.Length <= 100
        select new VisibleNotification { Notification = notification, TaskName = task.Name, ProjectName = project.Name };

    private static NotificationInboxItemDto ToDto(VisibleNotification value) => new(value.Notification.Id,
        value.Notification.Id, value.Notification.Kind, value.Notification.TaskId, value.Notification.ProjectId,
        value.TaskName, value.ProjectName, new DateTimeOffset(NotificationPayloadValidator.AsUtc(value.Notification.OccurredAtUtc)),
        value.Notification.ReadAtUtc is { } readAt ? new DateTimeOffset(NotificationPayloadValidator.AsUtc(readAt)) : null);

    private sealed class VisibleNotification
    {
        public UserNotification Notification { get; init; } = null!;
        public string TaskName { get; init; } = null!;
        public string ProjectName { get; init; } = null!;
    }
}
