using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class MatchCheckIn
{
    public int CheckInId { get; set; }

    public int MatchId { get; set; }

    public int PlayerId { get; set; }

    public int? StaffId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CheckedInAt { get; set; }

    public virtual Match Match { get; set; } = null!;

    public virtual Player Player { get; set; } = null!;

    public virtual Staff? Staff { get; set; }
}
