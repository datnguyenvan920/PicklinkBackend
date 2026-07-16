namespace PicklinkBackend.Models;

public sealed class BookingSlot
{
    public int BookingSlotId { get; set; }
    public int BookingId { get; set; }
    public int CourtId { get; set; }
    public int? CheckInGroupId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double HourlyPriceSnapshot { get; set; }
    public double CourtAmount { get; set; }
    public Booking Booking { get; set; } = null!;
    public Court Court { get; set; } = null!;
    public BookingCheckInGroup? CheckInGroup { get; set; }
}
