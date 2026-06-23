namespace PicklinkBackend.Models;

public class BookingOperation
{
    public int BookingOperationId { get; set; }
    public int BookingId { get; set; }
    public string CheckInStatus { get; set; } = "Ready";
    public DateTime? CodeVerifiedAt { get; set; }
    public int? CodeVerifiedByUserId { get; set; }
    public DateTime? PaymentConfirmedAt { get; set; }
    public int? PaymentConfirmedByUserId { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public int? CheckedInByUserId { get; set; }
    public DateTime? NoShowAt { get; set; }
    public int? NoShowByUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Booking Booking { get; set; } = null!;
}
