using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Payment
{
    public int PaymentId { get; set; }

    public int BookingId { get; set; }

    public int PayerId { get; set; }

    public Guid? PaymentGroupId { get; set; }

    public decimal Amount { get; set; }

    public string PaymentMethod { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? PaidAt { get; set; }

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

    public int? VerifiedByUserId { get; set; }

    public string? RejectionReason { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual Player Payer { get; set; } = null!;

    public virtual ICollection<PaymentStatusHistory> StatusHistories { get; set; } = new List<PaymentStatusHistory>();

    public virtual ICollection<SePayTransaction> SePayTransactions { get; set; } = new List<SePayTransaction>();

    public virtual SessionTicket? SessionTicket { get; set; }
}
