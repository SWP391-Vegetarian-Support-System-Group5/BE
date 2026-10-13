using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BLL.Services;

namespace API.Services;

public sealed class CommunityEventStream : ICommunityEventPublisher
{
    private readonly ConcurrentDictionary<Guid, Channel<CommunityEvent>> subscribers = new();

    public Task PublishAsync(CommunityEvent communityEvent, CancellationToken cancellationToken = default)
    {
        foreach (var subscriber in subscribers.Values)
        {
            subscriber.Writer.TryWrite(communityEvent);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<CommunityEvent> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<CommunityEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        subscribers[id] = channel;
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }
        finally
        {
            subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }
}
