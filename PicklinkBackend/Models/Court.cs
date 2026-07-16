using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Court
{
    public int CourtId { get; set; }

    public int VenueId { get; set; }

    public int CourtNumber { get; set; }

    public string? SurfaceType { get; set; }

    public string? CourtType { get; set; }

    public decimal HourlyPrice { get; set; }

    public bool IsIndoor { get; set; }

    public string AvailabilityStatus { get; set; } = null!;

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<BookingSlot> BookingSlots { get; set; } = new List<BookingSlot>();

    public virtual ICollection<BookingCheckInGroup> BookingCheckInGroups { get; set; } = new List<BookingCheckInGroup>();

    public virtual ICollection<Scorecard> Scorecards { get; set; } = new List<Scorecard>();

    public virtual Venue Venue { get; set; } = null!;
}
