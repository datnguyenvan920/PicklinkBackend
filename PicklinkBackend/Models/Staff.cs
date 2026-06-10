using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Staff
{
    public int StaffId { get; set; }

    public int UserId { get; set; }

    public int VenueId { get; set; }

    public string Role { get; set; } = null!;

    public virtual ICollection<MatchCheckIn> MatchCheckIns { get; set; } = new List<MatchCheckIn>();

    public virtual User User { get; set; } = null!;

    public virtual Venue Venue { get; set; } = null!;
}
