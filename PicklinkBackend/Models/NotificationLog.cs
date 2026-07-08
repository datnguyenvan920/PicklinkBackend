using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class NotificationLog
{
    public int NotifId { get; set; }

    public int UserId { get; set; }

    public string Message { get; set; } = null!;

    public bool IsRead { get; set; }

    public string NotificationType { get; set; } = "system";

    public string Title { get; set; } = "ThÃ´ng bÃ¡o";

    public string Tone { get; set; } = "default";

    public string? LinkTo { get; set; }

    public string? LinkLabel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual User User { get; set; } = null!;
}
