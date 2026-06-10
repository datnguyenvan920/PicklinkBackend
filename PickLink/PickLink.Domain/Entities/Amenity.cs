using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class Amenity
{
    public int AmenityId { get; set; }
    public int VenueId { get; set; }
    public string AmenityName { get; set; } = string.Empty;
    public bool IsFree { get; set; } = true;

    public Venue Venue { get; set; } = null!;
}
