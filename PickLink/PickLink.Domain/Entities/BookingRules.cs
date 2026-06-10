using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class BookingRules
{
    public int RuleId { get; set; }
    public int VenueId { get; set; }
    public string RuleType { get; set; } = string.Empty;
    public string RuleContent { get; set; } = string.Empty;

    public Venue Venue { get; set; } = null!;
}
