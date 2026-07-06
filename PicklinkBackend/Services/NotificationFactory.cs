using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public static class NotificationTypes
{
    public const string Match = "match";
    public const string Payment = "payment";
    public const string Court = "court";
    public const string Club = "club";
    public const string System = "system";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Match,
        Payment,
        Court,
        Club,
        System
    };
}

public static class NotificationTones
{
    public const string Default = "default";
    public const string Urgent = "urgent";
    public const string Success = "success";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Default,
        Urgent,
        Success
    };
}

public sealed record NotificationInput(
    int UserId,
    string Type,
    string Title,
    string Message,
    string Tone = NotificationTones.Default,
    string? LinkTo = null,
    string? LinkLabel = null);

public static class NotificationFactory
{
    public static NotificationLog Create(NotificationInput input, DateTime now)
    {
        if (input.UserId <= 0)
            throw new ArgumentException("Người nhận thông báo không hợp lệ.", nameof(input));

        var type = input.Type.Trim().ToLowerInvariant();
        if (!NotificationTypes.All.Contains(type))
            throw new ArgumentException("Loại thông báo không được hỗ trợ.", nameof(input));

        var title = input.Title.Trim();
        if (title.Length is < 1 or > 200)
            throw new ArgumentException("Tiêu đề thông báo phải có từ 1 đến 200 ký tự.", nameof(input));

        var message = input.Message.Trim();
        if (message.Length is < 1 or > 2000)
            throw new ArgumentException("Nội dung thông báo phải có từ 1 đến 2000 ký tự.", nameof(input));

        var tone = input.Tone.Trim().ToLowerInvariant();
        if (!NotificationTones.All.Contains(tone))
            throw new ArgumentException("Mức độ thông báo không được hỗ trợ.", nameof(input));

        return new NotificationLog
        {
            UserId = input.UserId,
            NotificationType = type,
            Title = title,
            Message = message,
            Tone = tone,
            LinkTo = Normalize(input.LinkTo),
            LinkLabel = Normalize(input.LinkLabel),
            CreatedAt = now,
            IsRead = false
        };
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
