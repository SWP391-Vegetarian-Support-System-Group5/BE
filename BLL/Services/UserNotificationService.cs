using System.Collections.Concurrent;
using BLL.Common;
using BLL.DTOs;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

// Bounded process-local inbox; nothing is written to the database.
public sealed class UserNotificationService(INotificationEventPublisher publisher, ILogger<UserNotificationService> logger) : IUserNotificationService
{
    private readonly ConcurrentDictionary<int, List<UserNotificationResponse>> _inboxes = new();
    private long _nextId;

    public async Task CreateAsync(int userId, string type, string message, int? postId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var notification = new UserNotificationResponse(Interlocked.Increment(ref _nextId), type, message, postId, false, VietnamTime.Now);
        var inbox = _inboxes.GetOrAdd(userId, _ => []);
        lock (inbox)
        {
            inbox.Insert(0, notification);
            if (inbox.Count > 100) inbox.RemoveRange(100, inbox.Count - 100);
        }
        try { await publisher.SendAsync(userId, notification, cancellationToken); }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(error, "Could not push notification {Id}; it remains available until the API restarts.", notification.Id);
        }
    }

    public Task<IReadOnlyCollection<UserNotificationResponse>> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_inboxes.TryGetValue(userId, out var inbox)) return Task.FromResult<IReadOnlyCollection<UserNotificationResponse>>([]);
        lock (inbox) return Task.FromResult<IReadOnlyCollection<UserNotificationResponse>>(inbox.ToArray());
    }

    public Task<int> CountUnreadAsync(int userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_inboxes.TryGetValue(userId, out var inbox)) return Task.FromResult(0);
        lock (inbox) return Task.FromResult(inbox.Count(item => !item.IsRead));
    }

    public Task MarkReadAsync(int userId, long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_inboxes.TryGetValue(userId, out var inbox)) throw new ServiceException("Không tìm thấy thông báo.", 404);
        lock (inbox)
        {
            var index = inbox.FindIndex(item => item.Id == id);
            if (index < 0) throw new ServiceException("Không tìm thấy thông báo.", 404);
            inbox[index] = inbox[index] with { IsRead = true };
        }
        return Task.CompletedTask;
    }
}
