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

    public string? OwnerEntryType { get; set; }

    public string? Title { get; set; }

    public string? BookingCode { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? HoldExpiresAt { get; set; }

    public double HourlyPriceSnapshot { get; set; }

    public double CourtAmount { get; set; }

    public double TotalAmount { get; set; }

    public virtual Court Court { get; set; } = null!;

    public virtual Match? Match { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<BookingStatusHistory> StatusHistories { get; set; } = new List<BookingStatusHistory>();

    public virtual BookingOperation? Operation { get; set; }

    public virtual Player? Player { get; set; }
}
