namespace PicklinkBackend.DTOs;

public sealed class ListingFeeSettingsRequest
{
    public decimal PricePerCourtPerMonth { get; set; }
}

public sealed class ListingFeeSettingsResponse
{
    public int ListingFeeSettingId { get; set; }
    public decimal PricePerCourtPerMonth { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class ListingFeePaymentRejectionRequest
{
    public string? Reason { get; set; }
}

public sealed class AdminListingFeePaymentResponse
{
    public int VenueListingPaymentId { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public int Months { get; set; }
    public int ActiveCourtCount { get; set; }
    public decimal PricePerCourtPerMonth { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ReceiptImageUrl { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? PaidFrom { get; set; }
    public DateTime? PaidUntil { get; set; }
}