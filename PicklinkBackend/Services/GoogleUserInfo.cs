namespace PicklinkBackend.Services;

public sealed record GoogleUserInfo(
    string Subject,
    string Email,
    string? Name,
    string? Picture);
