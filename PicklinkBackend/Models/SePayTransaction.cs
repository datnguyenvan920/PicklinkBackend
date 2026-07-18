namespace PicklinkBackend.Models;

public sealed class SePayTransaction
{
    public int SePayTransactionId { get; set; }
    public long ExternalTransactionId { get; set; }
    public int PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string? ReferenceCode { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    public string? RefundReference { get; set; }

    public Payment Payment { get; set; } = null!;
}
