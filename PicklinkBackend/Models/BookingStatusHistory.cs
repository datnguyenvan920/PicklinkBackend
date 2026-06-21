namespace PicklinkBackend.Models;

public class BookingStatusHistory
{
    public int BookingStatusHistoryId { get; set; }
    public int BookingId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public int? ActorUserId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public virtual Booking Booking { get; set; } = null!;
}
