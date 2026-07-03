namespace PicklinkBackend.Models;

public class MatchAvailabilitySlot
{
    public int MatchAvailabilitySlotId { get; set; }

    public int MatchId { get; set; }

    public TimeOnly TimeStart { get; set; }

    public TimeOnly TimeEnd { get; set; }

    public virtual Match Match { get; set; } = null!;
}
