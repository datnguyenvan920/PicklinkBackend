namespace PicklinkBackend.Models;

public partial class MatchSlotVote
{
    public int MatchSlotVoteId { get; set; }

    public int MatchId { get; set; }

    public int PlayerId { get; set; }

    public int CourtId { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Match Match { get; set; } = null!;

    public virtual Player Player { get; set; } = null!;
}
