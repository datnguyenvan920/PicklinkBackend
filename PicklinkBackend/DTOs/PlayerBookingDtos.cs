using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class CreateBookingHoldRequest
{
    public int CourtId { get; set; }
    public DateOnly Date { get; set; }

    [Required, MinLength(1), MaxLength(16)]
    public List<TimeOnly> SlotStarts { get; set; } = [];

}

public class CompleteBookingPaymentRequest
{
    [Required, RegularExpression("^(Wallet|BankTransfer|AtCourt)$")]
    public string PaymentMethod { get; set; } = "Wallet";
}

public class PlayerVenueSummaryResponse
{
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double OverallRating { get; set; }
    public string OpenTime { get; set; } = string.Empty;
    public string CloseTime { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public double FromPrice { get; set; }
    public int CourtCount { get; set; }
}

public class PlayerCourtAvailabilityResponse
{
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string OpenTime { get; set; } = string.Empty;
    public string CloseTime { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public int SlotMinutes { get; set; } = 30;
    public List<PlayerCourtResponse> Courts { get; set; } = [];
    public List<PlayerAvailabilitySlotResponse> Slots { get; set; } = [];
}

public class PlayerCourtResponse
{
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public string CourtType { get; set; } = string.Empty;
    public string? SurfaceType { get; set; }
    public bool IsIndoor { get; set; }
    public double HourlyPrice { get; set; }
}

public class PlayerAvailabilitySlotResponse
{
    public int CourtId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = "Available";
    public int? BookingId { get; set; }
    public bool IsOwnedByCurrentUser { get; set; }
}

public class BookingHoldingResponse
{
    public int BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double DurationHours { get; set; }
    public double HourlyPrice { get; set; }
    public double CourtAmount { get; set; }
    public double TotalAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string CheckInStatus { get; set; } = string.Empty;
    public BankTransferResponse? BankTransfer { get; set; }
    public List<BookingStatusHistoryResponse> StatusHistory { get; set; } = [];
}

public class BookingStatusHistoryResponse
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime ChangedAt { get; set; }
}
