using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Booking
{
    public int BookingId { get; set; }

    public int? PlayerId { get; set; }

    public int CourtId { get; set; }

    public int? MatchId { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public string Status { get; set; } = null!;

    public virtual Court Court { get; set; } = null!;

    public virtual Match? Match { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual Player? Player { get; set; }
}
