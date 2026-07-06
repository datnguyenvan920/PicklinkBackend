using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PicklinkBackend.Services;

public sealed record NotificationChangedEvent(
    int UserId,
    int? NotificationId,
    string Action,
    DateTime ChangedAt);

public sealed class NotificationRealtimeNotifier
{
    private readonly ConcurrentDictionary<Guid, Subscriber> _subscribers = new();

    public NotificationRealtimeSubscription Subscribe(int userId)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<NotificationChangedEvent>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = new Subscriber(userId, channel);
        return new NotificationRealtimeSubscription(
            channel.Reader,
            () => _subscribers.TryRemove(id, out _));
    }

    public void Publish(int userId, int? notificationId, string action)
    {
        var notification = new NotificationChangedEvent(
            userId,
            notificationId,
            action,
            DateTime.UtcNow);
        foreach (var subscriber in _subscribers.Values)
        {
            if (subscriber.UserId == userId)
                subscriber.Channel.Writer.TryWrite(notification);
        }
    }

    private sealed record Subscriber(
        int UserId,
        Channel<NotificationChangedEvent> Channel);
}

public sealed class NotificationRealtimeSubscription : IDisposable
{
    private readonly Action _unsubscribe;
    private bool _disposed;

    public NotificationRealtimeSubscription(
        ChannelReader<NotificationChangedEvent> reader,
        Action unsubscribe)
    {
        Reader = reader;
        _unsubscribe = unsubscribe;
    }

    public ChannelReader<NotificationChangedEvent> Reader { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _unsubscribe();
    }
}
