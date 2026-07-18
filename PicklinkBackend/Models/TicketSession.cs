namespace PicklinkBackend.Models;

public sealed class TicketSession
{
    public int TicketSessionId { get; set; }
    public int BookingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SkillLevel { get; set; } = string.Empty;
    public string PlayFormat { get; set; } = string.Empty;
    public int MaxPlayers { get; set; }
    public decimal TicketPrice { get; set; }
    public int CancellationDeadlineHours { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    public Booking Booking { get; set; } = null!;
    public ICollection<SessionTicket> Tickets { get; set; } = new List<SessionTicket>();
}
