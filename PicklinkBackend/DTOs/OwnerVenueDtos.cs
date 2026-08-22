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
    public decimal? HourlyPrice { get; set; }

    public bool? IsIndoor { get; set; }

    [Required, RegularExpression("^(Available|Maintenance|Inactive)$")]
    public string AvailabilityStatus { get; set; } = "Available";
}

public class OwnerCourtCreateRequest : OwnerCourtUpsertRequest {}
public class OwnerCourtUpdateRequest : OwnerCourtUpsertRequest {}
public class OwnerBankAccountUpsertRequest : OwnerBankAccountRequest
{
    public string AccountNo { get => AccountNumber; set => AccountNumber = value; }
}

public class OwnerVenueImageUploadRequest
{
    [Required]
    public IFormFile Image { get; set; } = null!;

    public IFormFile File { get => Image; set => Image = value; }

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

    /// <summary>
    /// "Maintenance" is still accepted so older clients keep working, but it now behaves exactly
    /// like "Blocked": both simply take the slot away from players.
    /// </summary>
    [Required(ErrorMessage = "Vui lòng chọn loại lịch.")]
    [RegularExpression("^(Blocked|Maintenance|Event|WalkIn)$",
        ErrorMessage = "Loại lịch không hợp lệ. Chỉ nhận khóa khung giờ, đặt tại sân hoặc sự kiện.")]
    public string EntryType { get; set; } = "Blocked";

    [StringLength(200, ErrorMessage = "Ghi chú không được vượt quá 200 ký tự.")]
    public string? Title { get; set; }

    /// <summary>Set when the walk-in customer already has a player account.</summary>
    public int? CustomerPlayerId { get; set; }

    /// <summary>Typed at the counter when the customer has no account.</summary>
    [StringLength(200, ErrorMessage = "Tên khách không được vượt quá 200 ký tự.")]
    public string? CustomerName { get; set; }

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    [StringLength(30, ErrorMessage = "Số điện thoại không được vượt quá 30 ký tự.")]
    public string? CustomerPhone { get; set; }

    [Range(0, 100_000_000, ErrorMessage = "Số tiền phải từ 0 đến 100.000.000đ.")]
    public decimal? Amount { get; set; }

    [RegularExpression("^(Cash|BankTransfer|Unpaid)$",
        ErrorMessage = "Hình thức thanh toán không hợp lệ.")]
    public string? PaymentMethod { get; set; }
}

public class OwnerPlayerSearchResponse
{
    public int PlayerId { get; set; }
    public int UserId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
}

public class OwnerBookingStatusRequest
{
    [Required(ErrorMessage = "Thiếu trạng thái cần cập nhật.")]
    [RegularExpression("^(Confirmed|Cancelled)$", ErrorMessage = "Trạng thái booking không hợp lệ.")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Required when cancelling: it is shown to the player and kept in the audit trail.</summary>
    [StringLength(500, ErrorMessage = "Lý do không được vượt quá 500 ký tự.")]
    public string? Reason { get; set; }
}

public class OwnerBookingRefundRequest
{
    [Required]
    public IFormFile Proof { get; set; } = null!;

    [StringLength(200, ErrorMessage = "Ghi chú hoàn tiền không được vượt quá 200 ký tự.")]
    public string? Reference { get; set; }
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

public class OwnerVenueReviewResponse
{
    public int RatingId { get; set; }
    public int? BookingId { get; set; }
    public string ReviewerName { get; set; } = string.Empty;
    public int? CourtNumber { get; set; }
    public int Score { get; set; }
    public string? Comment { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool IsAnonymous { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OwnerListingFeePreviewResponse
{
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public int Months { get; set; }
    public int ActiveCourtCount { get; set; }
    public decimal PricePerCourtPerMonth { get; set; }
    public decimal Amount { get; set; }
    public decimal TotalAmount { get => Amount; set => Amount = value; }
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
    public string? CustomerPhone { get; set; }
    public int? CustomerUserId { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentStatus { get; set; }
    public string? CheckInStatus { get; set; }
    public bool CanCancel { get; set; } = true;
    /// <summary>Cancelling this booking leaves money to hand back.</summary>
    public bool RequiresRefund { get; set; }
    /// <summary>Already cancelled and waiting for the owner to settle the refund.</summary>
    public bool RefundPending { get; set; }
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
    public string? CheckInStatus { get; set; }
    public string? EntryType { get; set; }
    public string? Title { get; set; }
}
