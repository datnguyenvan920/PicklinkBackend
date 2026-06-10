using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Venue
{
    public int VenueId { get; set; }

    public int OwnerId { get; set; }

    public string VenueName { get; set; } = null!;

    public string Address { get; set; } = null!;

    public double OverallRating { get; set; }

    public TimeOnly OpenTime { get; set; }

    public TimeOnly CloseTime { get; set; }

    public string? PhoneNumber { get; set; }

    public virtual ICollection<Amenity> Amenities { get; set; } = new List<Amenity>();

    public virtual ICollection<BookingRule> BookingRules { get; set; } = new List<BookingRule>();

    public virtual ICollection<Court> Courts { get; set; } = new List<Court>();

    public virtual VenueOwner Owner { get; set; } = null!;

    public virtual ICollection<Staff> Staff { get; set; } = new List<Staff>();

    public virtual ICollection<VenueAuditLog> VenueAuditLogs { get; set; } = new List<VenueAuditLog>();
}
