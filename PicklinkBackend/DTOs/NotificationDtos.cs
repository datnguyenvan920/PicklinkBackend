using PicklinkBackend.Services;

namespace PicklinkBackend.DTOs;

public sealed class NotificationResponse
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = NotificationTypes.System;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Tone { get; set; } = NotificationTones.Default;
    public string? LinkTo { get; set; }
    public string? LinkLabel { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public sealed class NotificationUnreadCountResponse
{
    public int Count { get; set; }
}
