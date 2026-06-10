using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class BookingRule
{
    public int RuleId { get; set; }

    public int VenueId { get; set; }

    public string RuleType { get; set; } = null!;

    public string RuleContent { get; set; } = null!;

    public virtual Venue Venue { get; set; } = null!;
}
