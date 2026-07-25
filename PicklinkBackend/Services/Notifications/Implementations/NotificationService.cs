using PicklinkBackend.Models;
using PicklinkBackend.Repositories;

namespace PicklinkBackend.Services.Notifications.Implementations;

public sealed class NotificationService
{
    private readonly INotificationRepository _notificationRepository;
    private readonly NotificationRealtimeNotifier _realtime;
    private readonly List<NotificationLog> _pending = [];

    public NotificationService(
        INotificationRepository notificationRepository,
        NotificationRealtimeNotifier realtime)
    {
        _notificationRepository = notificationRepository;
        _realtime = realtime;
    }

    public NotificationLog Add(NotificationInput input)
    {
        var notification = NotificationFactory.Create(input, DateTime.UtcNow);
        _notificationRepository.AddNotificationAsync(notification);
        _pending.Add(notification);
        return notification;
    }

    public void PublishCreated(NotificationLog notification) =>
        _realtime.Publish(notification.UserId, notification.NotifId, "Created");

    public void PublishPending()
    {
        foreach (var notification in _pending)
        {
            PublishCreated(notification);
        }

        _pending.Clear();
    }

    public void PublishChanged(int userId, int? notificationId, string action) =>
        _realtime.Publish(userId, notificationId, action);
}
