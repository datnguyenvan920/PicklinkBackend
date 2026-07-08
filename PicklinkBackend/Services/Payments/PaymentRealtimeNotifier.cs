using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PicklinkBackend.Services.Payments;

public sealed record PaymentChangedEvent(
    int PaymentId,
    int BookingId,
    int VenueId,
    string PaymentStatus,
    string Action);

public sealed class PaymentRealtimeNotifier
{
    private readonly ConcurrentDictionary<Guid, Channel<PaymentChangedEvent>> _subscribers = new();

    public PaymentRealtimeSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<PaymentChangedEvent>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = channel;
        return new PaymentRealtimeSubscription(channel.Reader, () => _subscribers.TryRemove(id, out _));
    }

    public void Publish(PaymentChangedEvent notification)
    {
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryWrite(notification);
    }
}

public sealed class PaymentRealtimeSubscription : IDisposable
{
    private readonly Action _unsubscribe;
    private bool _disposed;

    public PaymentRealtimeSubscription(ChannelReader<PaymentChangedEvent> reader, Action unsubscribe)
    {
        Reader = reader;
        _unsubscribe = unsubscribe;
    }

    public ChannelReader<PaymentChangedEvent> Reader { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _unsubscribe();
    }
}
