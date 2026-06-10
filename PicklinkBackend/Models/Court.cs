using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Court
{
    public int CourtId { get; set; }

    public int VenueId { get; set; }

    public int CourtNumber { get; set; }

    public string? SurfaceType { get; set; }

    public bool IsIndoor { get; set; }

    public string AvailabilityStatus { get; set; } = null!;

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<Scorecard> Scorecards { get; set; } = new List<Scorecard>();

    public virtual Venue Venue { get; set; } = null!;
}
