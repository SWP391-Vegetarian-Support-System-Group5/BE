using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using Microsoft.Extensions.Logging.Abstractions;

static class NotificationTests
{
    public static async Task RunAsync()
    {
        var publisher = new NotificationPublisher();
        var service = new UserNotificationService(publisher, NullLogger<UserNotificationService>.Instance);
        await service.CreateAsync(1, "POST_RATED", "Có đánh giá mới.", 7);
        var notification = (await service.GetAsync(1)).Single();
        Check(notification.PostId == 7 && !notification.IsRead && publisher.Sent.Single().userId == 1, "buffer and push to owner");
        Check((await service.GetAsync(2)).Count == 0 && await service.CountUnreadAsync(2) == 0, "another user cannot list notification");
        try { await service.MarkReadAsync(2, notification.Id); throw new Exception("Expected access rejection"); }
        catch (ServiceException e) when (e.StatusCode == 404) { Check(true, "another user cannot mark notification read"); }
        await service.MarkReadAsync(1, notification.Id);
        Check(await service.CountUnreadAsync(1) == 0, "read state updates in memory");
        var afterRestart = new UserNotificationService(publisher, NullLogger<UserNotificationService>.Instance);
        Check((await afterRestart.GetAsync(1)).Count == 0, "notifications do not survive a service restart");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS notification " + message);
    }

    private sealed class NotificationPublisher : INotificationEventPublisher
    {
        public List<(int userId, UserNotificationResponse notification)> Sent { get; } = [];
        public Task SendAsync(int userId, UserNotificationResponse notification, CancellationToken cancellationToken = default)
        {
            Sent.Add((userId, notification));
            return Task.CompletedTask;
        }
    }
}
