using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class VenueOwner
{
    public int OwnerId { get; set; }

    public int UserId { get; set; }

    public string? SpecialPermissions { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual ICollection<Venue> Venues { get; set; } = new List<Venue>();
}
