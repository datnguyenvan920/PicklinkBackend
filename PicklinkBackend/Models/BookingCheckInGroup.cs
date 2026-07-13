namespace PicklinkBackend.Models;

public sealed class BookingCheckInGroup
{
    public int BookingCheckInGroupId { get; set; }
    public int BookingId { get; set; }
    public int CourtId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string CheckInCode { get; set; } = string.Empty;
    public string CheckInStatus { get; set; } = "Ready";
    public DateTime? CodeVerifiedAt { get; set; }
    public int? CodeVerifiedByUserId { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public int? CheckedInByUserId { get; set; }
    public DateTime? NoShowAt { get; set; }
    public int? NoShowByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Booking Booking { get; set; } = null!;
    public Court Court { get; set; } = null!;
    public ICollection<BookingSlot> Slots { get; set; } = new List<BookingSlot>();
}
