using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PicklinkBackend.DTOs;

public class OwnerVenueUpsertRequest
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string VenueName { get; set; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 5)]
    public string Address { get; set; } = string.Empty;

    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }

    [Phone, StringLength(30)]
    public string? PhoneNumber { get; set; }

    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    public double? Longitude { get; set; }

    [Range(0, 100_000_000)]
    public decimal BasePrice { get; set; }

    [Range(0, 100)]
    public int InitialCourtCount { get; set; }

    public List<string> Amenities { get; set; } = [];
}

public class OwnerCourtUpsertRequest
{
    [Range(1, 10_000)]
    public int CourtNumber { get; set; }

    [StringLength(100)]
    public string? SurfaceType { get; set; }

    [Required, StringLength(100, MinimumLength = 2)]
    public string CourtType { get; set; } = "Standard";

    [Range(0, 100_000_000)]
    public decimal HourlyPrice { get; set; }

    public bool IsIndoor { get; set; }

    [Required, RegularExpression("^(Available|Maintenance|Inactive)$")]
    public string AvailabilityStatus { get; set; } = "Available";
}

public class OwnerVenueImageUploadRequest
{
    [Required]
    public IFormFile Image { get; set; } = null!;

    [StringLength(200)]
    public string? Caption { get; set; }
}

public class OwnerVenueOpenStatusRequest
{
    public bool IsOpen { get; set; }
}

public class OwnerScheduleBlockRequest
{
    public int CourtId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    [Required, RegularExpression("^(Blocked|Maintenance|Event)$")]
    public string EntryType { get; set; } = "Blocked";

    [StringLength(200)]
    public string? Title { get; set; }
}

public class OwnerBookingStatusRequest
{
    [Required, RegularExpression("^(Confirmed|Cancelled)$")]
    public string Status { get; set; } = string.Empty;
}

public class OwnerVenueResponse
{
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double OverallRating { get; set; }
    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }
    public string? PhoneNumber { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public decimal BasePrice { get; set; }
    public bool IsOpen { get; set; }
    public string ApprovalStatus { get; set; } = "Draft";
    public string? RejectionReason { get; set; }
    public string ListingStatus { get; set; } = "Unpaid";
    public DateTime? ListingExpiresAt { get; set; }
    public OwnerListingFeePaymentResponse? LatestListingPayment { get; set; }
    public List<string> Amenities { get; set; } = [];
    public List<OwnerVenueImageResponse> Images { get; set; } = [];
    public List<OwnerCourtResponse> Courts { get; set; } = [];
}

public class OwnerListingFeePreviewResponse
{
    public int VenueId { get; set; }
    public int Months { get; set; }
    public int ActiveCourtCount { get; set; }
    public decimal PricePerCourtPerMonth { get; set; }
    public decimal Amount { get; set; }
}

public class OwnerListingFeePaymentResponse : OwnerListingFeePreviewResponse
{
    public int VenueListingPaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ReceiptImageUrl { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? PaidFrom { get; set; }
    public DateTime? PaidUntil { get; set; }
}

public class OwnerListingFeePaymentRequest
{
    [Range(1, 24)]
    public int Months { get; set; } = 1;

    [Required]
    public IFormFile? Receipt { get; set; }
}

public class OwnerVenueImageResponse
{
    public int VenueImageId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
}

public class OwnerCourtResponse
{
    public int CourtId { get; set; }
    public int VenueId { get; set; }
    public int CourtNumber { get; set; }
    public string? SurfaceType { get; set; }
    public string CourtType { get; set; } = string.Empty;
    public decimal HourlyPrice { get; set; }
    public bool IsIndoor { get; set; }
    public string AvailabilityStatus { get; set; } = string.Empty;
}

public class OwnerScheduleResponse
{
    public DateOnly Date { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string View { get; set; } = "day";
    public int SlotMinutes { get; set; } = 30;
    public List<OwnerVenueResponse> Venues { get; set; } = [];
    public List<OwnerScheduleItemResponse> Items { get; set; } = [];
    public List<OwnerScheduleSlotResponse> Slots { get; set; } = [];
}

public class OwnerScheduleItemResponse
{
    public int BookingId { get; set; }
    public int CourtId { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public int CourtNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentStatus { get; set; }
    public bool IsOwnerBlock { get; set; }
    public bool IsOwnerEntry { get; set; }
    public string? EntryType { get; set; }
    public string? Title { get; set; }
}

public class OwnerScheduleSlotResponse
{
    public int CourtId { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public int CourtNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = "Available";
    public int? BookingId { get; set; }
    public string? EntryType { get; set; }
    public string? Title { get; set; }
}
