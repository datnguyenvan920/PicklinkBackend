namespace PicklinkBackend.Services.Staff;

public enum StaffOperationResultStatus
{
    Success,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record StaffOperationResult<T>(
    StaffOperationResultStatus Status,
    T? Value = default,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == StaffOperationResultStatus.Success;

    public static StaffOperationResult<T> Success(T value) =>
        new(StaffOperationResultStatus.Success, Value: value);

    public static StaffOperationResult<T> BadRequest(string message) =>
        new(StaffOperationResultStatus.BadRequest, ErrorMessage: message);

    public static StaffOperationResult<T> Unauthorized() =>
        new(StaffOperationResultStatus.Unauthorized, ErrorMessage: "Vui long dang nhap.");

    public static StaffOperationResult<T> Forbidden(string message) =>
        new(StaffOperationResultStatus.Forbidden, ErrorMessage: message);

    public static StaffOperationResult<T> NotFound(string message) =>
        new(StaffOperationResultStatus.NotFound, ErrorMessage: message);

    public static StaffOperationResult<T> Conflict(string message) =>
        new(StaffOperationResultStatus.Conflict, ErrorMessage: message);
}
