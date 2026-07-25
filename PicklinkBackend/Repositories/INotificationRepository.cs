using PicklinkBackend.Models;

namespace PicklinkBackend.Repositories;

public interface INotificationRepository
{
    IQueryable<NotificationLog> Notifications { get; }
    Task<NotificationLog?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default);
    Task AddNotificationAsync(NotificationLog notification, CancellationToken cancellationToken = default);
    Task RemoveNotificationAsync(NotificationLog notification, CancellationToken cancellationToken = default);
    Task RemoveNotificationsAsync(IEnumerable<NotificationLog> notifications, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
