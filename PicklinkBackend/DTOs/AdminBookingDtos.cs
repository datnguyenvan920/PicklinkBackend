namespace PicklinkBackend.DTOs;

public sealed class AdminBookingSummaryResponse
{
    public int BookingId { get; set; }
    public string? BookingCode { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public DateTime CreatedAt { get; set; }
    public double TotalAmount { get; set; }
    public double CourtAmount { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string? PlayerEmail { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public DateTime? PaymentSubmittedAt { get; set; }
    public DateTime? PaymentVerifiedAt { get; set; }
}