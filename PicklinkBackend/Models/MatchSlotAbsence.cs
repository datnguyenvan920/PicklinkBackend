namespace PicklinkBackend.Models;

public sealed class MatchSlotAbsence
{
    public int MatchSlotAbsenceId { get; set; }
    public int MatchId { get; set; }
    public int BookingCheckInGroupId { get; set; }
    public int UnavailablePlayerId { get; set; }
    public string Status { get; set; } = "Open";
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Match Match { get; set; } = null!;
    public BookingCheckInGroup BookingCheckInGroup { get; set; } = null!;
    public Player UnavailablePlayer { get; set; } = null!;
    public ICollection<MatchSlotReplacementRequest> ReplacementRequests { get; set; } = new List<MatchSlotReplacementRequest>();
}
