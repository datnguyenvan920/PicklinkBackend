namespace PicklinkBackend.Services.Players;

public enum PlayerProfileResultStatus
{
    Success,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record PlayerProfileResult<T>(
    PlayerProfileResultStatus Status,
    T? Value = default,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == PlayerProfileResultStatus.Success;

    public static PlayerProfileResult<T> Success(T value) =>
        new(PlayerProfileResultStatus.Success, Value: value);

    public static PlayerProfileResult<T> BadRequest(string message) =>
        new(PlayerProfileResultStatus.BadRequest, ErrorMessage: message);

    public static PlayerProfileResult<T> Unauthorized() =>
        new(PlayerProfileResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static PlayerProfileResult<T> Forbidden(string message) =>
        new(PlayerProfileResultStatus.Forbidden, ErrorMessage: message);

    public static PlayerProfileResult<T> NotFound() =>
        new(PlayerProfileResultStatus.NotFound, ErrorMessage: "Không tìm thấy người dùng.");

    public static PlayerProfileResult<T> Conflict(string message) =>
        new(PlayerProfileResultStatus.Conflict, ErrorMessage: message);
}
