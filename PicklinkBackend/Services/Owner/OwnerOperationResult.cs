namespace PicklinkBackend.Services.Owner;

public enum OwnerOperationResultStatus
{
    Success,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record OwnerOperationResult<T>(
    OwnerOperationResultStatus Status,
    T? Value = default,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == OwnerOperationResultStatus.Success;

    public static OwnerOperationResult<T> Success(T value) =>
        new(OwnerOperationResultStatus.Success, Value: value);

    public static OwnerOperationResult<T> BadRequest(string message) =>
        new(OwnerOperationResultStatus.BadRequest, ErrorMessage: message);

    public static OwnerOperationResult<T> Unauthorized() =>
        new(OwnerOperationResultStatus.Unauthorized, ErrorMessage: "Vui long dang nhap.");

    public static OwnerOperationResult<T> Forbidden(string message) =>
        new(OwnerOperationResultStatus.Forbidden, ErrorMessage: message);

    public static OwnerOperationResult<T> NotFound(string message) =>
        new(OwnerOperationResultStatus.NotFound, ErrorMessage: message);

    public static OwnerOperationResult<T> Conflict(string message) =>
        new(OwnerOperationResultStatus.Conflict, ErrorMessage: message);
}
