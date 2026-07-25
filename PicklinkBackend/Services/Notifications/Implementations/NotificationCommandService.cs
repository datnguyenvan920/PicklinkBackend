using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Notifications;

namespace PicklinkBackend.Services.Notifications.Implementations;

public sealed class NotificationCommandService
{
    private readonly INotificationRepository _notificationRepository;
    private readonly NotificationService _notifications;

    public NotificationCommandService(
        INotificationRepository notificationRepository,
        NotificationService notifications)
    {
        _notificationRepository = notificationRepository;
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
            await _notificationRepository.SaveChangesAsync(cancellationToken);
            _notifications.PublishChanged(userId, notificationId, "Read");
        }

        return NotificationCommandResult.Success(Map(notification));
    }

    public async Task MarkAllAsReadAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.Notifications
            .Where(notification => notification.UserId == userId && !notification.IsRead)
            .ToListAsync(cancellationToken);
        if (notifications.Count == 0) return;

        foreach (var notification in notifications) notification.IsRead = true;
        await _notificationRepository.SaveChangesAsync(cancellationToken);
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

        await _notificationRepository.RemoveNotificationAsync(notification, cancellationToken);
        await _notificationRepository.SaveChangesAsync(cancellationToken);
        _notifications.PublishChanged(userId, notificationId, "Deleted");
        return NotificationCommandResult.Deleted();
    }

    public async Task DeleteReadAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.Notifications
            .Where(notification => notification.UserId == userId && notification.IsRead)
            .ToListAsync(cancellationToken);
        if (notifications.Count == 0) return;

        await _notificationRepository.RemoveNotificationsAsync(notifications, cancellationToken);
        await _notificationRepository.SaveChangesAsync(cancellationToken);
        _notifications.PublishChanged(userId, null, "DeletedRead");
    }

    private Task<NotificationLog?> LoadUserNotification(
        int userId,
        int notificationId,
        CancellationToken cancellationToken)
    {
        return _notificationRepository.Notifications.SingleOrDefaultAsync(
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
