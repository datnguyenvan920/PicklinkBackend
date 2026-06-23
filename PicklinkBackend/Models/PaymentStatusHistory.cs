namespace PicklinkBackend.Models;

public class PaymentStatusHistory
{
    public int PaymentStatusHistoryId { get; set; }
    public int PaymentId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public int? ActorUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Payment Payment { get; set; } = null!;
}
