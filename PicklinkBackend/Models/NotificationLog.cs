using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class NotificationLog
{
    public int NotifId { get; set; }

    public int UserId { get; set; }

    public string Message { get; set; } = null!;

    public bool IsRead { get; set; }

    public virtual User User { get; set; } = null!;
}
