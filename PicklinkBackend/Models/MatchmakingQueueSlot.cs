using System;

namespace PicklinkBackend.Models;

public partial class MatchmakingQueueSlot
{
    public int MatchmakingQueueSlotId { get; set; }

    public int MatchmakingQueueId { get; set; }

    public DayOfWeek? DayOfWeek { get; set; }

    public DateOnly? SpecificDate { get; set; }

    public int? DayOfMonth { get; set; }

    public TimeOnly TimeStart { get; set; }

    public TimeOnly TimeEnd { get; set; }

    public virtual MatchmakingQueue MatchmakingQueue { get; set; } = null!;
}
