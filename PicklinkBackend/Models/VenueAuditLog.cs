using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class VenueAuditLog
{
    public int LogId { get; set; }

    public int VenueId { get; set; }

    public int ActorId { get; set; }

    public string Action { get; set; } = null!;

    public DateTime Timestamp { get; set; }

    public virtual User Actor { get; set; } = null!;

    public virtual Venue Venue { get; set; } = null!;
}
