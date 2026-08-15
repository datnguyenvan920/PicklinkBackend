namespace PicklinkBackend.DTOs;

/// <summary>
/// Stable, language-independent identifiers the frontend branches on.
/// The accompanying `message` is written for humans and may be translated at any
/// time; these codes are part of the API contract and must not change.
/// </summary>
public static class ApiErrorCodes
{
    public const string CloudinaryNotConfigured = "CLOUDINARY_NOT_CONFIGURED";
    public const string PhoneNumberRequired = "PHONE_NUMBER_REQUIRED";
}
