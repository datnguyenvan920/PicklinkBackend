using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PicklinkBackend.Services.Schedules;

public sealed record ScheduleChangedEvent(
    int VenueId,
    int CourtId,
    DateTime StartTime,
    DateTime EndTime,
    string EntryType,
    string Action);

public sealed class ScheduleRealtimeNotifier
{
    private readonly ConcurrentDictionary<Guid, Channel<ScheduleChangedEvent>> _subscribers = new();

    public ScheduleRealtimeSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<ScheduleChangedEvent>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = channel;
        return new ScheduleRealtimeSubscription(channel.Reader, () => _subscribers.TryRemove(id, out _));
    }

    public void Publish(ScheduleChangedEvent notification)
    {
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(notification);
        }
    }
}

public sealed class ScheduleRealtimeSubscription : IDisposable
{
    private readonly Action _unsubscribe;
    private bool _disposed;

    public ScheduleRealtimeSubscription(ChannelReader<ScheduleChangedEvent> reader, Action unsubscribe)
    {
        Reader = reader;
        _unsubscribe = unsubscribe;
    }

    public ChannelReader<ScheduleChangedEvent> Reader { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _unsubscribe();
    }
}
