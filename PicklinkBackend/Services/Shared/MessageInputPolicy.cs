namespace PicklinkBackend.Services.Shared;

public static class MessageInputPolicy
{
    public const int MaximumContentLength = 2000;
    public const int MaximumMediaUrlLength = 500;

    public static MessageInputValidationResult Validate(string? content, string? mediaUrl = null)
    {
        var normalizedContent = Normalize(content);
        var normalizedMediaUrl = Normalize(mediaUrl);

        if (normalizedContent is null && normalizedMediaUrl is null)
            return MessageInputValidationResult.Invalid("Nội dung tin nhắn hoặc tệp đính kèm là bắt buộc.");

        if (normalizedContent?.Length > MaximumContentLength)
            return MessageInputValidationResult.Invalid(
                $"Nội dung tin nhắn không được vượt quá {MaximumContentLength} ký tự.");

        if (normalizedMediaUrl?.Length > MaximumMediaUrlLength)
            return MessageInputValidationResult.Invalid(
                $"Đường dẫn tệp đính kèm không được vượt quá {MaximumMediaUrlLength} ký tự.");

        if (normalizedMediaUrl is not null && !IsSafeMediaUrl(normalizedMediaUrl))
            return MessageInputValidationResult.Invalid("Đường dẫn tệp đính kèm không hợp lệ.");

        return MessageInputValidationResult.Valid(normalizedContent, normalizedMediaUrl);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsSafeMediaUrl(string value)
    {
        if (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal))
            return true;

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}

public sealed record MessageInputValidationResult(
    bool IsValid,
    string? Content,
    string? MediaUrl,
    string? ErrorMessage)
{
    public static MessageInputValidationResult Valid(string? content, string? mediaUrl) =>
        new(true, content, mediaUrl, ErrorMessage: null);

    public static MessageInputValidationResult Invalid(string errorMessage) =>
        new(false, Content: null, MediaUrl: null, errorMessage);
}
