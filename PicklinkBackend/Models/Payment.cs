using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Payment
{
    public int PaymentId { get; set; }

    public int BookingId { get; set; }

    public int PayerId { get; set; }

    public double Amount { get; set; }

    public string PaymentMethod { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? PaidAt { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual Player Payer { get; set; } = null!;
}
