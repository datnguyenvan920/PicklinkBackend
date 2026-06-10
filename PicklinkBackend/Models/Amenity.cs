using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Amenity
{
    public int AmenityId { get; set; }

    public int VenueId { get; set; }

    public string AmenityName { get; set; } = null!;

    public bool IsFree { get; set; }

    public virtual Venue Venue { get; set; } = null!;
}
