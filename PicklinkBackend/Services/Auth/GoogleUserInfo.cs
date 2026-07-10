namespace PicklinkBackend.Services.Auth;

public sealed record GoogleUserInfo(
    string Subject,
    string Email,
    string? Name,
    string? Picture);
