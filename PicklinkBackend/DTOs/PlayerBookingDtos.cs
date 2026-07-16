using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class CreateBookingHoldSlotRequest
{
    public int CourtId { get; set; }
    public TimeOnly StartTime { get; set; }
}

public class CreateBookingHoldRequest
{
    public DateOnly Date { get; set; }

    [Required, MinLength(1), MaxLength(16)]
    public List<CreateBookingHoldSlotRequest> Slots { get; set; } = [];
}

public class CompleteBookingPaymentRequest
{
    [Required, RegularExpression("^(Wallet|BankTransfer|AtCourt)$")]
    public string PaymentMethod { get; set; } = "Wallet";
}

public class CancelPlayerBookingRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
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
    public decimal FromPrice { get; set; }
    public int CourtCount { get; set; }
    public bool IsFavorite { get; set; }
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
    public decimal HourlyPrice { get; set; }
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
    public decimal HourlyPrice { get; set; }
    public decimal CourtAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string CheckInStatus { get; set; } = string.Empty;
    public DateTime? CheckedInAt { get; set; }
    public string? CheckInCode { get; set; }
    public bool CanCancel { get; set; }
    public bool CanRetryPayment { get; set; }
    public bool CanReview { get; set; }
    public bool HasReviewed { get; set; }
    public BankTransferResponse? BankTransfer { get; set; }
    public List<BookingStatusHistoryResponse> StatusHistory { get; set; } = [];
    public List<BookingSlotResponse> Slots { get; set; } = [];
    public List<BookingCheckInGroupResponse> CheckInGroups { get; set; } = [];
}

public class BookingSlotResponse
{
    public int BookingSlotId { get; set; }
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public int? CheckInGroupId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public decimal HourlyPrice { get; set; }
    public decimal CourtAmount { get; set; }
}

public class BookingCheckInGroupResponse
{
    public int BookingCheckInGroupId { get; set; }
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? CheckInCode { get; set; }
    public string CheckInStatus { get; set; } = string.Empty;
    public DateTime? CheckedInAt { get; set; }
}

public class BookingHoldingGroupResponse
{
    public Guid PaymentGroupId { get; set; }
    public decimal TotalAmount { get; set; }
    public List<BookingHoldingResponse> Bookings { get; set; } = [];
}

public class CreateBookingReviewRequest
{
    [Range(1, 5)]
    public int Score { get; set; }

    [StringLength(1000)]
    public string? Comment { get; set; }

    [MaxLength(10)]
    public List<string> Tags { get; set; } = [];

    public bool IsAnonymous { get; set; }
}

public class BookingReviewResponse
{
    public int RatingId { get; set; }
    public int BookingId { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public int Score { get; set; }
    public string? Comment { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool IsAnonymous { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BookingStatusHistoryResponse
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime ChangedAt { get; set; }
}
