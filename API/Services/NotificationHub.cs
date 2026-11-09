using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace API.Services;

[Authorize]
public sealed class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{Context.User!.GetRequiredUserId()}");
        await base.OnConnectedAsync();
    }
}

public sealed class SignalRNotificationPublisher(IHubContext<NotificationHub> hub) : INotificationEventPublisher
{
    public Task SendAsync(int userId, UserNotificationResponse notification, CancellationToken cancellationToken = default) =>
        hub.Clients.Group($"user:{userId}").SendAsync("notificationReceived", notification, cancellationToken);
}
