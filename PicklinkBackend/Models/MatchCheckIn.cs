using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class MatchCheckIn
{
    public int CheckInId { get; set; }

    public int MatchId { get; set; }

    public int PlayerId { get; set; }

    public int? StaffId { get; set; }

    /// <summary>
    /// The check-in code the player was scanned against. One code covers a single booking round
    /// on a single court over adjacent slots, so attendance is tracked per code, not per match.
    /// Null on rows written before attendance was split per code.
    /// </summary>
    public int? BookingCheckInGroupId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CheckedInAt { get; set; }

    public virtual Match Match { get; set; } = null!;

    public virtual Player Player { get; set; } = null!;

    public virtual Staff? Staff { get; set; }

    public virtual BookingCheckInGroup? BookingCheckInGroup { get; set; }
}
