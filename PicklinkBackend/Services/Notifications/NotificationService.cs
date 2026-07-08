using PicklinkBackend.Data;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Notifications;

public sealed class NotificationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly NotificationRealtimeNotifier _realtime;
    private readonly List<NotificationLog> _pending = [];

    public NotificationService(
        ApplicationDbContext dbContext,
        NotificationRealtimeNotifier realtime)
    {
        _dbContext = dbContext;
        _realtime = realtime;
    }

    public NotificationLog Add(NotificationInput input)
    {
        var notification = NotificationFactory.Create(input, DateTime.UtcNow);
        _dbContext.NotificationLogs.Add(notification);
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
