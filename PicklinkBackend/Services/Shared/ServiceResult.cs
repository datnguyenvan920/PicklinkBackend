namespace PicklinkBackend.Services.Shared;

public enum ServiceResultStatus
{
    Success,
    Created,
    NoContent,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    StatusCode
}

public sealed record ServiceResult(
    ServiceResultStatus Status,
    object? Value = null,
    object? Error = null,
    string? CreatedActionName = null,
    object? CreatedRouteValues = null,
    int? RawStatusCode = null);

public sealed record ServiceResult<T>(
    ServiceResultStatus Status,
    T? Value = default,
    object? Error = null,
    string? CreatedActionName = null,
    object? CreatedRouteValues = null,
    int? RawStatusCode = null)
{
    public static implicit operator ServiceResult<T>(ServiceResult result) =>
        new(
            result.Status,
            result.Value is T value ? value : default,
            result.Error,
            result.CreatedActionName,
            result.CreatedRouteValues,
            result.RawStatusCode);
}