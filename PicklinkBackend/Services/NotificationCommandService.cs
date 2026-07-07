using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public sealed class NotificationCommandService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly NotificationService _notifications;

    public NotificationCommandService(
        ApplicationDbContext dbContext,
        NotificationService notifications)
    {
        _dbContext = dbContext;
        _notifications = notifications;
    }

    public async Task<NotificationCommandResult> MarkAsReadAsync(
        int userId,
        int notificationId,
        CancellationToken cancellationToken)
    {
        var notification = await LoadUserNotification(userId, notificationId, cancellationToken);
        if (notification is null)
            return NotificationCommandResult.NotFound("Không tìm thấy thông báo.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await _dbContext.SaveChangesAsync(cancellationToken);
            _notifications.PublishChanged(userId, notificationId, "Read");
        }

        return NotificationCommandResult.Success(Map(notification));
    }

    public async Task MarkAllAsReadAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var notifications = await _dbContext.NotificationLogs
            .Where(notification => notification.UserId == userId && !notification.IsRead)
            .ToListAsync(cancellationToken);
        if (notifications.Count == 0) return;

        foreach (var notification in notifications) notification.IsRead = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        _notifications.PublishChanged(userId, null, "ReadAll");
    }

    public async Task<NotificationCommandResult> DeleteAsync(
        int userId,
        int notificationId,
        CancellationToken cancellationToken)
    {
        var notification = await LoadUserNotification(userId, notificationId, cancellationToken);
        if (notification is null)
            return NotificationCommandResult.NotFound("Không tìm thấy thông báo.");

        _dbContext.NotificationLogs.Remove(notification);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _notifications.PublishChanged(userId, notificationId, "Deleted");
        return NotificationCommandResult.Deleted();
    }

    public async Task DeleteReadAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var notifications = await _dbContext.NotificationLogs
            .Where(notification => notification.UserId == userId && notification.IsRead)
            .ToListAsync(cancellationToken);
        if (notifications.Count == 0) return;

        _dbContext.NotificationLogs.RemoveRange(notifications);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _notifications.PublishChanged(userId, null, "DeletedRead");
    }

    private async Task<NotificationLog?> LoadUserNotification(
        int userId,
        int notificationId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.NotificationLogs.SingleOrDefaultAsync(
            notification => notification.NotifId == notificationId
                && notification.UserId == userId,
            cancellationToken);
    }

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

public sealed record NotificationCommandResult(
    NotificationCommandResultStatus Status,
    NotificationResponse? Notification = null,
    string? ErrorMessage = null)
{
    public static NotificationCommandResult Success(NotificationResponse notification) =>
        new(NotificationCommandResultStatus.Success, notification, ErrorMessage: null);

    public static NotificationCommandResult Deleted() =>
        new(NotificationCommandResultStatus.Deleted);

    public static NotificationCommandResult NotFound(string errorMessage) =>
        new(NotificationCommandResultStatus.NotFound, Notification: null, ErrorMessage: errorMessage);
}

public enum NotificationCommandResultStatus
{
    Success,
    Deleted,
    NotFound
}
