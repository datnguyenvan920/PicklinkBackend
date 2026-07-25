using PicklinkBackend.Services.Notifications;

namespace PicklinkBackend.DTOs;

public class NotificationResponse
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

public class NotificationLogResponse : NotificationResponse
{
    public static NotificationLogResponse FromResponse(NotificationResponse response) => new()
    {
        NotificationId = response.NotificationId,
        Type = response.Type,
        Title = response.Title,
        Message = response.Message,
        Tone = response.Tone,
        LinkTo = response.LinkTo,
        LinkLabel = response.LinkLabel,
        CreatedAt = response.CreatedAt,
        IsRead = response.IsRead
    };
}

public class NotificationListResponse
{
    public List<NotificationResponse> Items { get; set; } = [];
    public int UnreadCount { get; set; }

    public static implicit operator NotificationListResponse(PaginatedResponse<NotificationResponse> paginated) => new()
    {
        Items = paginated.Items.ToList(),
        UnreadCount = paginated.TotalCount
    };

    public static implicit operator NotificationListResponse(PaginatedResponse<NotificationLogResponse> paginated) => new()
    {
        Items = paginated.Items.Cast<NotificationResponse>().ToList(),
        UnreadCount = paginated.TotalCount
    };
}

public sealed class NotificationUnreadCountResponse
{
    public int Count { get; set; }
}
