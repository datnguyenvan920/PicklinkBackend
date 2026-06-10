using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class Court
{
    public int CourtId { get; set; }
    public int VenueId { get; set; }
    public int CourtNumber { get; set; }
    public string? SurfaceType { get; set; }
    public bool IsIndoor { get; set; } = false;
    public string AvailabilityStatus { get; set; } = "Available";
    public int? MaxHourlyCapacity { get; set; }

    public Venue Venue { get; set; } = null!;
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<Scorecard> Scorecards { get; set; } = new List<Scorecard>();
}
