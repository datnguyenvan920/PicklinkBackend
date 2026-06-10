using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class NotificationLog
{
    public int NotifId { get; set; }
    public int UserId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string NotifType { get; set; } = "General"; // Match | Booking | System
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
