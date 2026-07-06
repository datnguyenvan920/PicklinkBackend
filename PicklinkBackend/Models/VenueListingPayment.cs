namespace PicklinkBackend.Models;

public partial class VenueListingPayment
{
    public int VenueListingPaymentId { get; set; }

    public int VenueId { get; set; }

    public int Months { get; set; }

    public int ActiveCourtCount { get; set; }

    public decimal PricePerCourtPerMonth { get; set; }

    public decimal Amount { get; set; }

    public string Status { get; set; } = "PendingReview";

    public string? ReceiptImageUrl { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime SubmittedAt { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public int? ReviewedByUserId { get; set; }

    public DateTime? PaidFrom { get; set; }

    public DateTime? PaidUntil { get; set; }

    public virtual Venue Venue { get; set; } = null!;

    public virtual User? ReviewedByUser { get; set; }
}
