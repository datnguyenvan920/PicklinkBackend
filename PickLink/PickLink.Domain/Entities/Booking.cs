using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

using PickLink.Domain.Enums;

public class Booking
{
    public int BookingId { get; set; }
    public int? PlayerId { get; set; }
    public int CourtId { get; set; }
    public int? MatchId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }  // CHECK EndTime > StartTime enforced in DbContext
    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    public Player? Player { get; set; }
    public Court Court { get; set; } = null!;
    public Match? Match { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
