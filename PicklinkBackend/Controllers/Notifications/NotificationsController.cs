using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly NotificationService _notifications;

    public NotificationsController(
        ApplicationDbContext dbContext,
        NotificationService notifications)
    {
        _dbContext = dbContext;
        _notifications = notifications;
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

        var normalizedType = type?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedType)
            && normalizedType != "all"
            && !NotificationTypes.All.Contains(normalizedType))
        {
            return BadRequest(new { message = "Loại thông báo không hợp lệ." });
        }

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var query = _dbContext.NotificationLogs
            .AsNoTracking()
            .Where(notification => notification.UserId == userId.Value);
        if (unreadOnly)
            query = query.Where(notification => !notification.IsRead);
        if (!string.IsNullOrWhiteSpace(normalizedType) && normalizedType != "all")
            query = query.Where(notification => notification.NotificationType == normalizedType);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.NotifId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(notification => new NotificationResponse
            {
                NotificationId = notification.NotifId,
                Type = notification.NotificationType,
                Title = notification.Title,
                Message = notification.Message,
                Tone = notification.Tone,
                LinkTo = notification.LinkTo,
                LinkLabel = notification.LinkLabel,
                CreatedAt = notification.CreatedAt,
                IsRead = notification.IsRead
            })
            .ToListAsync(cancellationToken);

        return Ok(Pagination.Create(items, totalCount, page, pageSize));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<NotificationUnreadCountResponse>> GetUnreadCount(
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var count = await _dbContext.NotificationLogs
            .CountAsync(
                notification => notification.UserId == userId.Value && !notification.IsRead,
                cancellationToken);
        return Ok(new NotificationUnreadCountResponse { Count = count });
    }

    [HttpPatch("{notificationId:int}/read")]
    public async Task<ActionResult<NotificationResponse>> MarkAsRead(
        int notificationId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var notification = await _dbContext.NotificationLogs.SingleOrDefaultAsync(
            notification => notification.NotifId == notificationId
                && notification.UserId == userId.Value,
            cancellationToken);
        if (notification is null) return NotFound(new { message = "Không tìm thấy thông báo." });

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await _dbContext.SaveChangesAsync(cancellationToken);
            _notifications.PublishChanged(userId.Value, notificationId, "Read");
        }
        return Ok(Map(notification));
    }

    [HttpPatch("read-all")]
    public async Task<ActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var notifications = await _dbContext.NotificationLogs
            .Where(notification => notification.UserId == userId.Value && !notification.IsRead)
            .ToListAsync(cancellationToken);
        if (notifications.Count == 0) return NoContent();

        foreach (var notification in notifications) notification.IsRead = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        _notifications.PublishChanged(userId.Value, null, "ReadAll");
        return NoContent();
    }

    [HttpDelete("{notificationId:int}")]
    public async Task<ActionResult> DeleteNotification(
        int notificationId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var notification = await _dbContext.NotificationLogs.SingleOrDefaultAsync(
            notification => notification.NotifId == notificationId
                && notification.UserId == userId.Value,
            cancellationToken);
        if (notification is null) return NotFound(new { message = "Không tìm thấy thông báo." });

        _dbContext.NotificationLogs.Remove(notification);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _notifications.PublishChanged(userId.Value, notificationId, "Deleted");
        return NoContent();
    }

    [HttpDelete("read")]
    public async Task<ActionResult> DeleteReadNotifications(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var notifications = await _dbContext.NotificationLogs
            .Where(notification => notification.UserId == userId.Value && notification.IsRead)
            .ToListAsync(cancellationToken);
        if (notifications.Count == 0) return NoContent();

        _dbContext.NotificationLogs.RemoveRange(notifications);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _notifications.PublishChanged(userId.Value, null, "DeletedRead");
        return NoContent();
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;

    private static NotificationResponse Map(NotificationLog notification) => new()
    {
        NotificationId = notification.NotifId,
        Type = notification.NotificationType,
        Title = notification.Title,
        Message = notification.Message,
        Tone = notification.Tone,
        LinkTo = notification.LinkTo,
        LinkLabel = notification.LinkLabel,
        CreatedAt = notification.CreatedAt,
        IsRead = notification.IsRead
    };
}

public sealed class NotificationResponse
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = NotificationTypes.System;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Tone { get; set; } = NotificationTones.Default;
    public string? LinkTo { get; set; }
    public string? LinkLabel { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public sealed class NotificationUnreadCountResponse
{
    public int Count { get; set; }
}
