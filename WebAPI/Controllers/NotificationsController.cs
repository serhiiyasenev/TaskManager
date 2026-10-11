using BLL.Common;
using BLL.Interfaces;
using BLL.Models.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController(INotificationInboxService inbox) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotificationInboxPageDto>> Get(int page = 1, int pageSize = 20,
        bool unreadOnly = false, CancellationToken ct = default)
        => (await inbox.GetPageAsync(page, pageSize, unreadOnly, ct)).ToActionResult();

    [HttpPut("{notificationId:guid}/read")]
    public async Task<ActionResult<NotificationInboxItemDto>> MarkRead(Guid notificationId, CancellationToken ct)
        => (await inbox.MarkReadAsync(notificationId, ct)).ToActionResult();
}
