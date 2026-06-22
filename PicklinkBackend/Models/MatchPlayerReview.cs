namespace PicklinkBackend.Models;

public class MatchPlayerReview
{
    public int MatchPlayerReviewId { get; set; }

    public int MatchId { get; set; }

    public int ReviewerPlayerId { get; set; }

    public int RevieweePlayerId { get; set; }

    public int Score { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Match Match { get; set; } = null!;

    public virtual Player ReviewerPlayer { get; set; } = null!;

    public virtual Player RevieweePlayer { get; set; } = null!;
}
