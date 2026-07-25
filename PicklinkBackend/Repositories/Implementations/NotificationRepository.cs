using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.Models;

namespace PicklinkBackend.Repositories.Implementations;

public class NotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _dbContext;

    public NotificationRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IQueryable<NotificationLog> Notifications => _dbContext.NotificationLogs;

    public Task<NotificationLog?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        return _dbContext.NotificationLogs
            .SingleOrDefaultAsync(n => n.NotifId == notificationId, cancellationToken);
    }

    public async Task AddNotificationAsync(NotificationLog notification, CancellationToken cancellationToken = default)
    {
        await _dbContext.NotificationLogs.AddAsync(notification, cancellationToken);
    }

    public Task RemoveNotificationAsync(NotificationLog notification, CancellationToken cancellationToken = default)
    {
        _dbContext.NotificationLogs.Remove(notification);
        return Task.CompletedTask;
    }

    public Task RemoveNotificationsAsync(IEnumerable<NotificationLog> notifications, CancellationToken cancellationToken = default)
    {
        _dbContext.NotificationLogs.RemoveRange(notifications);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
