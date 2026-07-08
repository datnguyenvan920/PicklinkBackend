using PicklinkBackend.Data;

namespace PicklinkBackend.Services;

public sealed record CommunityServiceDependencies(ApplicationDbContext DbContext, NotificationService Notifications);

public partial class CommunityService
{
    private const string PublicGroup = "Public";
    private const string PrivateGroup = "Private";
    private const string AcceptedStatus = "Accepted";
    private const string PendingStatus = "Pending";
    private const string DeclinedStatus = "Declined";
    private const string BannedStatus = "Banned";
    private const string OwnerRole = "Owner";
    private const string AdminRole = "Admin";
    private const string ModeratorRole = "Moderator";
    private const string MemberRole = "Member";

    private readonly ApplicationDbContext _dbContext;
    private readonly NotificationService _notifications;
    private int? _currentUserId;

    public CommunityService(
        ApplicationDbContext dbContext,
        NotificationService notifications)
    {
        _dbContext = dbContext;
        _notifications = notifications;
    }

    public void SetCurrentUserId(int? userId)
    {
        _currentUserId = userId;
    }

    private static CommunityServiceResult<IReadOnlyList<T>> Ok<T>(List<T> value) =>
        new(CommunityServiceResultStatus.Success, value);

    private static CommunityServiceResult<T> Ok<T>(T value) =>
        new(CommunityServiceResultStatus.Success, value);

    private static CommunityServiceResult Ok() =>
        new(CommunityServiceResultStatus.Success);

    private static CommunityServiceResult NoContent() =>
        new(CommunityServiceResultStatus.NoContent);

    private static CommunityServiceResult BadRequest(object? body) =>
        new(CommunityServiceResultStatus.BadRequest, ErrorBody: body);

    private static CommunityServiceResult Unauthorized(object? body = null) =>
        new(CommunityServiceResultStatus.Unauthorized, ErrorBody: body);

    private static CommunityServiceResult Forbidden(object? body = null) =>
        new(CommunityServiceResultStatus.Forbidden, ErrorBody: body);

    private static CommunityServiceResult Forbid() => Forbidden();

    private static CommunityServiceResult NotFound(object? body = null) =>
        new(CommunityServiceResultStatus.NotFound, ErrorBody: body);

    private static CommunityServiceResult StatusCode(int statusCode, object? body = null) =>
        statusCode == StatusCodes.Status403Forbidden
            ? Forbidden(body)
            : new CommunityServiceResult(CommunityServiceResultStatus.ServerError, ErrorBody: body);

    private static CommunityServiceResult<T> CreatedAtAction<T>(
        string actionName,
        object? routeValues,
        T value) =>
        new(CommunityServiceResultStatus.Created, value, CreatedActionName: actionName, CreatedRouteValues: routeValues);
}

public sealed record CommunityServiceResult(
    CommunityServiceResultStatus Status,
    object? Value = null,
    object? ErrorBody = null,
    string? CreatedActionName = null,
    object? CreatedRouteValues = null)
{
    public static implicit operator CommunityServiceResult<object>(CommunityServiceResult result) =>
        new(result.Status, result.Value, result.ErrorBody, result.CreatedActionName, result.CreatedRouteValues);
}

public sealed record CommunityServiceResult<T>(
    CommunityServiceResultStatus Status,
    T? Value = default,
    object? ErrorBody = null,
    string? CreatedActionName = null,
    object? CreatedRouteValues = null)
{
    public static implicit operator CommunityServiceResult<T>(CommunityServiceResult result) =>
        new(result.Status, default, result.ErrorBody, result.CreatedActionName, result.CreatedRouteValues);
}

public enum CommunityServiceResultStatus
{
    Success,
    Created,
    NoContent,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    ServerError
}