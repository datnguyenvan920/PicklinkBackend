using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

using PickLink.Domain.Enums;

public class Payment
{
    public int PaymentId { get; set; }
    public int BookingId { get; set; }
    public int PayerId { get; set; }
    public decimal Amount { get; set; }   // decimal, NOT float — financial accuracy
    public string PaymentMethod { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTime? PaidAt { get; set; }

    public Booking Booking { get; set; } = null!;
    public Player Payer { get; set; } = null!;
}
