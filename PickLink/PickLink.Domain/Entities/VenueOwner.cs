using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class VenueOwner
{
    public int OwnerId { get; set; }
    public int UserId { get; set; }
    public string? SpecialPermissions { get; set; }

    public User User { get; set; } = null!;
    public ICollection<Venue> Venues { get; set; } = new List<Venue>();
}
