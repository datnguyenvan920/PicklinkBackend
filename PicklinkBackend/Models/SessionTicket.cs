namespace PicklinkBackend.Models;

public sealed class SessionTicket
{
    public int SessionTicketId { get; set; }
    public int TicketSessionId { get; set; }
    public int PlayerId { get; set; }
    public int PaymentId { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public string Status { get; set; } = "PendingPayment";
    public DateTime? HoldExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public int? CheckedInByStaffId { get; set; }

    public TicketSession TicketSession { get; set; } = null!;
    public Player Player { get; set; } = null!;
    public Payment Payment { get; set; } = null!;
    public Staff? CheckedInByStaff { get; set; }
}
