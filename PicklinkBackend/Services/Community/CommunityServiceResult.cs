namespace PicklinkBackend.Services.Community;

public enum CommunityServiceResultStatus
{
    Success,
    Created,
    NoContent,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    ServerError
}

public sealed record CommunityServiceResult(
    CommunityServiceResultStatus Status,
    object? ErrorBody = null,
    string? CreatedActionName = null,
    object? CreatedRouteValues = null,
    object? Value = null)
{
    public bool IsSuccess => Status is CommunityServiceResultStatus.Success
        or CommunityServiceResultStatus.Created
        or CommunityServiceResultStatus.NoContent;
}

public sealed record CommunityServiceResult<T>(
    CommunityServiceResultStatus Status,
    T? Value = default,
    object? ErrorBody = null,
    string? CreatedActionName = null,
    object? CreatedRouteValues = null)
{
    public bool IsSuccess => Status is CommunityServiceResultStatus.Success
        or CommunityServiceResultStatus.Created
        or CommunityServiceResultStatus.NoContent;

    public static implicit operator CommunityServiceResult<T>(CommunityServiceResult result) =>
        new(result.Status, ErrorBody: result.ErrorBody, CreatedActionName: result.CreatedActionName, CreatedRouteValues: result.CreatedRouteValues, Value: result.Value is T val ? val : default);
}

public enum DirectConversationServiceResultStatus
{
    Success,
    Created,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record DirectConversationServiceResult<T>(
    DirectConversationServiceResultStatus Status,
    T? Value = default,
    object? ErrorBody = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status is DirectConversationServiceResultStatus.Success or DirectConversationServiceResultStatus.Created;

    public static DirectConversationServiceResult<T> Success(T value) =>
        new(DirectConversationServiceResultStatus.Success, Value: value);

    public static DirectConversationServiceResult<T> Created(T value) =>
        new(DirectConversationServiceResultStatus.Created, Value: value);

    public static DirectConversationServiceResult<T> BadRequest(object? error = null) =>
        new(DirectConversationServiceResultStatus.BadRequest, ErrorBody: error, ErrorMessage: error?.ToString());

    public static DirectConversationServiceResult<T> Unauthorized(object? error = null) =>
        new(DirectConversationServiceResultStatus.Unauthorized, ErrorBody: error);

    public static DirectConversationServiceResult<T> Forbidden(object? error = null) =>
        new(DirectConversationServiceResultStatus.Forbidden, ErrorBody: error);

    public static DirectConversationServiceResult<T> NotFound(object? error = null) =>
        new(DirectConversationServiceResultStatus.NotFound, ErrorBody: error, ErrorMessage: error?.ToString());

    public static DirectConversationServiceResult<T> Conflict(object? error = null) =>
        new(DirectConversationServiceResultStatus.Conflict, ErrorBody: error, ErrorMessage: error?.ToString());
}
