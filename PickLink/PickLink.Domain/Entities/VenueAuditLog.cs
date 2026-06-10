using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class VenueAuditLog
{
    public int LogId { get; set; }
    public int VenueId { get; set; }
    public int ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public Venue Venue { get; set; } = null!;
    public User Actor { get; set; } = null!;
}
