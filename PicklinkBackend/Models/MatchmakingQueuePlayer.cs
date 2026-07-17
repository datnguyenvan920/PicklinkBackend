using System;

namespace PicklinkBackend.Models;

public partial class MatchmakingQueuePlayer
{
    public int MatchmakingQueuePlayerId { get; set; }

    public int MatchmakingQueueId { get; set; }

    public int PlayerId { get; set; }

    public bool IsHost { get; set; }

    public string Status { get; set; } = "Approved";

    public virtual MatchmakingQueue MatchmakingQueue { get; set; } = null!;

    public virtual Player Player { get; set; } = null!;
}
