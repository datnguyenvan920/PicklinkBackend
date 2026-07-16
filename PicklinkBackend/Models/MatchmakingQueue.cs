using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class MatchmakingQueue
{
    public int MatchmakingQueueId { get; set; }

    public string MatchType { get; set; } = null!;

    public int SkillLevel { get; set; }

    public double? SearchLatitude { get; set; }

    public double? SearchLongitude { get; set; }

    public double SearchRadiusKm { get; set; }

    public bool IsActive { get; set; } = true;

    public string ReplayType { get; set; } = "None";

    public string? ReplayWeekdays { get; set; }

    public bool IsPublic { get; set; } = false;

    public string? Province { get; set; }

    public string? Ward { get; set; }

    public string? SharedVenues { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<MatchmakingQueueSlot> QueueSlots { get; set; } = new List<MatchmakingQueueSlot>();

    public virtual ICollection<MatchmakingQueuePlayer> QueuePlayers { get; set; } = new List<MatchmakingQueuePlayer>();

    public virtual ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
}
