using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Notifications;

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
            throw new ArgumentException("NgÃ†Â°Ã¡Â»Âi nhÃ¡ÂºÂ­n thÃƒÂ´ng bÃƒÂ¡o khÃƒÂ´ng hÃ¡Â»Â£p lÃ¡Â»â€¡.", nameof(input));

        var type = input.Type.Trim().ToLowerInvariant();
        if (!NotificationTypes.All.Contains(type))
            throw new ArgumentException("LoÃ¡ÂºÂ¡i thÃƒÂ´ng bÃƒÂ¡o khÃƒÂ´ng Ã„â€˜Ã†Â°Ã¡Â»Â£c hÃ¡Â»â€” trÃ¡Â»Â£.", nameof(input));

        var title = input.Title.Trim();
        if (title.Length is < 1 or > 200)
            throw new ArgumentException("TiÃƒÂªu Ã„â€˜Ã¡Â»Â thÃƒÂ´ng bÃƒÂ¡o phÃ¡ÂºÂ£i cÃƒÂ³ tÃ¡Â»Â« 1 Ã„â€˜Ã¡ÂºÂ¿n 200 kÃƒÂ½ tÃ¡Â»Â±.", nameof(input));

        var message = input.Message.Trim();
        if (message.Length is < 1 or > 2000)
            throw new ArgumentException("NÃ¡Â»â„¢i dung thÃƒÂ´ng bÃƒÂ¡o phÃ¡ÂºÂ£i cÃƒÂ³ tÃ¡Â»Â« 1 Ã„â€˜Ã¡ÂºÂ¿n 2000 kÃƒÂ½ tÃ¡Â»Â±.", nameof(input));

        var tone = input.Tone.Trim().ToLowerInvariant();
        if (!NotificationTones.All.Contains(tone))
            throw new ArgumentException("MÃ¡Â»Â©c Ã„â€˜Ã¡Â»â„¢ thÃƒÂ´ng bÃƒÂ¡o khÃƒÂ´ng Ã„â€˜Ã†Â°Ã¡Â»Â£c hÃ¡Â»â€” trÃ¡Â»Â£.", nameof(input));

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
