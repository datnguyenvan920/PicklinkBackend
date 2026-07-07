using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly NotificationQueryService _queries;
    private readonly NotificationCommandService _commands;

    public NotificationsController(
        NotificationQueryService queries,
        NotificationCommandService commands)
    {
        _queries = queries;
        _commands = commands;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<NotificationResponse>>> GetNotifications(
        string? type,
        bool unreadOnly = false,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _queries.ListAsync(
            userId.Value,
            type,
            unreadOnly,
            page,
            pageSize,
            cancellationToken);
        return result.IsInvalidType
            ? BadRequest(new { message = result.ErrorMessage })
            : Ok(result.Notifications);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<NotificationUnreadCountResponse>> GetUnreadCount(
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        return Ok(await _queries.CountUnreadAsync(userId.Value, cancellationToken));
    }

    [HttpPatch("{notificationId:int}/read")]
    public async Task<ActionResult<NotificationResponse>> MarkAsRead(
        int notificationId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _commands.MarkAsReadAsync(
            userId.Value,
            notificationId,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPatch("read-all")]
    public async Task<ActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        await _commands.MarkAllAsReadAsync(userId.Value, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{notificationId:int}")]
    public async Task<ActionResult> DeleteNotification(
        int notificationId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _commands.DeleteAsync(userId.Value, notificationId, cancellationToken);
        return result.Status == NotificationCommandResultStatus.NotFound
            ? NotFound(new { message = result.ErrorMessage })
            : NoContent();
    }

    [HttpDelete("read")]
    public async Task<ActionResult> DeleteReadNotifications(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        await _commands.DeleteReadAsync(userId.Value, cancellationToken);
        return NoContent();
    }

    private ActionResult<NotificationResponse> ToActionResult(NotificationCommandResult result) =>
        result.Status switch
        {
            NotificationCommandResultStatus.Success => Ok(result.Notification),
            NotificationCommandResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
}
