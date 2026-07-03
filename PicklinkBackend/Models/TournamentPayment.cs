namespace PicklinkBackend.Models;

public class TournamentPayment
{
    public int TournamentPaymentId { get; set; }

    public int TournamentRegistrationId { get; set; }

    public decimal Amount { get; set; }

    public string PaymentMethod { get; set; } = null!;

    public string? TransferContent { get; set; }

    public string? ReceiptImageUrl { get; set; }

    public string Status { get; set; } = "Pending";

    public DateTime SubmittedAt { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public int? VerifiedByUserId { get; set; }

    public string? RejectionReason { get; set; }

    public virtual TournamentRegistration Registration { get; set; } = null!;
}
