namespace PicklinkBackend.Models;

public sealed class MatchSlotReplacementRequest
{
    public int MatchSlotReplacementRequestId { get; set; }
    public int MatchSlotAbsenceId { get; set; }
    public int PlayerId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime RequestedAt { get; set; }
    public DateTime? RespondedAt { get; set; }

    public MatchSlotAbsence MatchSlotAbsence { get; set; } = null!;
    public Player Player { get; set; } = null!;
}
