using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class Venue
{
    public int VenueId { get; set; }
    public int OwnerId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal OverallRating { get; set; } = 0.0m;
    public TimeOnly OpenTime { get; set; }     // BR-11
    public TimeOnly CloseTime { get; set; }    // BR-11
    public string? PhoneNumber { get; set; }
    public decimal? Latitude { get; set; }     // BR-18: Geofencing
    public decimal? Longitude { get; set; }    // BR-18: Geofencing

    public VenueOwner Owner { get; set; } = null!;
    public ICollection<Court> Courts { get; set; } = new List<Court>();
    public ICollection<Staff> Staff { get; set; } = new List<Staff>();
    public ICollection<Amenity> Amenities { get; set; } = new List<Amenity>();
    public ICollection<BookingRules> BookingRules { get; set; } = new List<BookingRules>();
    public ICollection<VenueAuditLog> AuditLogs { get; set; } = new List<VenueAuditLog>();
}
