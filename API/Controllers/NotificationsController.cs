using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController(IUserNotificationService notifications) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyCollection<UserNotificationResponse>> Get(CancellationToken ct) => notifications.GetAsync(User.GetRequiredUserId(), ct);
    [HttpGet("unread-count")] public async Task<IActionResult> Count(CancellationToken ct) => Ok(new { count = await notifications.CountUnreadAsync(User.GetRequiredUserId(), ct) });
    [HttpPut("{id:long}/read")] public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(User.GetRequiredUserId(), id, ct);
        return NoContent();
    }
}
