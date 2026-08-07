namespace PicklinkBackend.Services.Auth;

public enum AuthServiceResultStatus
{
    Success,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    ServerError,
    Problem
}

public sealed record AuthServiceResult<T>(
    AuthServiceResultStatus Status,
    T? Value = default,
    string? ErrorMessage = null,
    string? Title = null)
{
    public bool IsSuccess => Status == AuthServiceResultStatus.Success;

    public static AuthServiceResult<T> Success(T value) =>
        new(AuthServiceResultStatus.Success, Value: value);

    public static AuthServiceResult<T> BadRequest(string message) =>
        new(AuthServiceResultStatus.BadRequest, ErrorMessage: message);

    public static AuthServiceResult<T> Unauthorized(string? message = null) =>
        new(AuthServiceResultStatus.Unauthorized, ErrorMessage: message ?? "Vui lòng đăng nhập.");

    public static AuthServiceResult<T> Forbidden(string? message = null) =>
        new(AuthServiceResultStatus.Forbidden, ErrorMessage: message ?? "Không có quyền truy cập.");

    public static AuthServiceResult<T> NotFound(string? message = null) =>
        new(AuthServiceResultStatus.NotFound, ErrorMessage: message ?? "Không tìm thấy.");

    public static AuthServiceResult<T> Conflict(string message) =>
        new(AuthServiceResultStatus.Conflict, ErrorMessage: message);

    public static AuthServiceResult<T> ServerError(string message) =>
        new(AuthServiceResultStatus.ServerError, ErrorMessage: message);

    public static AuthServiceResult<T> Problem(string title, string detail) =>
        new(AuthServiceResultStatus.Problem, ErrorMessage: detail, Title: title);

    public static AuthServiceResult<T> From<U>(AuthServiceResult<U> source) =>
        new(source.Status, ErrorMessage: source.ErrorMessage, Title: source.Title);
}

