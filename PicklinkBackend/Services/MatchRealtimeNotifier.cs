using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PicklinkBackend.Services;

public sealed record MatchChangedEvent(int MatchId, string Action, DateTime ChangedAt);

public sealed class MatchRealtimeNotifier
{
    private readonly ConcurrentDictionary<Guid, Channel<MatchChangedEvent>> _subscribers = new();

    public MatchRealtimeSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<MatchChangedEvent>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = channel;
        return new MatchRealtimeSubscription(channel.Reader, () => _subscribers.TryRemove(id, out _));
    }

    public void Publish(int matchId, string action)
    {
        var notification = new MatchChangedEvent(matchId, action, DateTime.UtcNow);
        foreach (var channel in _subscribers.Values) channel.Writer.TryWrite(notification);
    }
}

public sealed class MatchRealtimeSubscription : IDisposable
{
    private readonly Action _unsubscribe;
    private bool _disposed;

    public MatchRealtimeSubscription(ChannelReader<MatchChangedEvent> reader, Action unsubscribe)
    {
        Reader = reader;
        _unsubscribe = unsubscribe;
    }

    public ChannelReader<MatchChangedEvent> Reader { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _unsubscribe();
    }
}
