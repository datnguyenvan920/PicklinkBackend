using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class Staff
{
    public int StaffId { get; set; }
    public int UserId { get; set; }
    public int VenueId { get; set; }
    public string Role { get; set; } = string.Empty; // MaintenanceStaff | BookingAgent | VenueAdmin

    public User User { get; set; } = null!;
    public Venue Venue { get; set; } = null!;
    public ICollection<MatchCheckin> Checkins { get; set; } = new List<MatchCheckin>();
}
