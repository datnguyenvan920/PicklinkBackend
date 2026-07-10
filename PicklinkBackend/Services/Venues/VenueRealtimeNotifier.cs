using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PicklinkBackend.Services.Venues;

public sealed record VenueChangedEvent(int VenueId, string Action, DateTime ChangedAt);

public sealed class VenueRealtimeNotifier
{
    private readonly ConcurrentDictionary<Guid, Channel<VenueChangedEvent>> _subscribers = new();

    public VenueRealtimeSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<VenueChangedEvent>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = channel;
        return new VenueRealtimeSubscription(channel.Reader, () => _subscribers.TryRemove(id, out _));
    }

    public void Publish(int venueId, string action)
    {
        var notification = new VenueChangedEvent(venueId, action, DateTime.UtcNow);
        foreach (var channel in _subscribers.Values) channel.Writer.TryWrite(notification);
    }
}

public sealed class VenueRealtimeSubscription : IDisposable
{
    private readonly Action _unsubscribe;
    private bool _disposed;

    public VenueRealtimeSubscription(ChannelReader<VenueChangedEvent> reader, Action unsubscribe)
    {
        Reader = reader;
        _unsubscribe = unsubscribe;
    }

    public ChannelReader<VenueChangedEvent> Reader { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _unsubscribe();
    }
}
