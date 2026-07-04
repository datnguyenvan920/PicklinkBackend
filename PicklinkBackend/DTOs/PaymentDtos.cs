using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class OwnerBankAccountRequest
{
    [Required, MaxLength(30)]
    public string BankCode { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string BankName { get; set; } = string.Empty;

    [Required, RegularExpression("^[0-9]{5,30}$")]
    public string AccountNumber { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string AccountHolderName { get; set; } = string.Empty;
}

public class OwnerBankAccountResponse
{
    public int OwnerBankAccountId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class RejectPaymentRequest
{
    [Required, MinLength(3), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class SubmitPaymentReceiptRequest
{
    public int? PayerId { get; set; }

    [Required]
    public IFormFile Receipt { get; set; } = null!;
}

public class BatchPaymentPreviewRequest
{
    [Required, MinLength(1)]
    public List<int> PayerIds { get; set; } = [];
}

public class BatchPaymentPreviewResponse
{
    public int BookingId { get; set; }
    public List<int> PayerIds { get; set; } = [];
    public List<string> MemberNames { get; set; } = [];
    public double TotalAmount { get; set; }
    public string TransferContent { get; set; } = string.Empty;
    public string QrImageUrl { get; set; } = string.Empty;
}

public class SubmitBatchPaymentReceiptRequest
{
    [Required, MinLength(1)]
    public List<int> PayerIds { get; set; } = [];

    [Required]
    public IFormFile Receipt { get; set; } = null!;
}

public class BatchPaymentResponse
{
    public Guid PaymentGroupId { get; set; }
    public double TotalAmount { get; set; }
    public List<BankTransferResponse> Payments { get; set; } = [];
}

public class PaymentHistoryResponse
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BankTransferResponse
{
    public int PaymentId { get; set; }
    public Guid? PaymentGroupId { get; set; }
    public int GroupPaymentCount { get; set; }
    public double GroupTotalAmount { get; set; }
    public int BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public string BookingStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string? TransferCode { get; set; }
    public string? TransferContent { get; set; }
    public string? BankCode { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountName { get; set; }
    public string? QrImageUrl { get; set; }
    public string? ReceiptImageUrl { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public int CourtNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public List<PaymentHistoryResponse> History { get; set; } = [];
}
