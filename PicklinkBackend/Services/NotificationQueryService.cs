using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services;

public sealed class NotificationQueryService
{
    private readonly ApplicationDbContext _dbContext;

    public NotificationQueryService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<NotificationListResult> ListAsync(
        int userId,
        string? type,
        bool unreadOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedType = type?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedType)
            && normalizedType != "all"
            && !NotificationTypes.All.Contains(normalizedType))
        {
            return NotificationListResult.InvalidType("Loại thông báo không hợp lệ.");
        }

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var query = _dbContext.NotificationLogs
            .AsNoTracking()
            .Where(notification => notification.UserId == userId);
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

        return NotificationListResult.Success(Pagination.Create(items, totalCount, page, pageSize));
    }

    public async Task<NotificationUnreadCountResponse> CountUnreadAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var count = await _dbContext.NotificationLogs
            .CountAsync(
                notification => notification.UserId == userId && !notification.IsRead,
                cancellationToken);
        return new NotificationUnreadCountResponse { Count = count };
    }
}

public sealed record NotificationListResult(
    PaginatedResponse<NotificationResponse>? Notifications,
    string? ErrorMessage)
{
    public bool IsInvalidType => ErrorMessage is not null;

    public static NotificationListResult Success(PaginatedResponse<NotificationResponse> notifications) =>
        new(notifications, ErrorMessage: null);

    public static NotificationListResult InvalidType(string errorMessage) =>
        new(Notifications: null, ErrorMessage: errorMessage);
}
