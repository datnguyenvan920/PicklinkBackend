using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Community;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Notifications.Implementations;

namespace PicklinkBackend.Services.Community.Implementations;

public sealed record CommunityServiceDependencies(
    ICommunityRepository CommunityRepository,
    NotificationService Notifications);

public partial class CommunityService : ICommunityService
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

    private readonly ICommunityRepository _communityRepository;
    private readonly NotificationService _notifications;
    private int? _currentUserId;

    private CommunityService(
        ICommunityRepository communityRepository,
        NotificationService notifications)
    {
        _communityRepository = communityRepository;
        _notifications = notifications;
    }

    public CommunityService(CommunityServiceDependencies dependencies)
        : this(dependencies.CommunityRepository, dependencies.Notifications)
    {
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
