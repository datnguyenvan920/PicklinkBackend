using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Tests;

public class InAppNotificationTests
{
    [Fact]
    public void FactoryCreatesACompleteUnreadNotification()
    {
        var now = new DateTime(2026, 7, 6, 9, 30, 0, DateTimeKind.Utc);

        var notification = NotificationFactory.Create(
            new NotificationInput(
                UserId: 12,
                Type: NotificationTypes.Match,
                Title: "  Lời mời thi đấu  ",
                Message: "  Minh mời bạn tham gia trận đấu.  ",
                Tone: NotificationTones.Urgent,
                LinkTo: "/matches/45",
                LinkLabel: "Xem trận"),
            now);

        Assert.Equal(12, notification.UserId);
        Assert.Equal("match", notification.NotificationType);
        Assert.Equal("Lời mời thi đấu", notification.Title);
        Assert.Equal("Minh mời bạn tham gia trận đấu.", notification.Message);
        Assert.Equal("urgent", notification.Tone);
        Assert.Equal("/matches/45", notification.LinkTo);
        Assert.Equal("Xem trận", notification.LinkLabel);
        Assert.Equal(now, notification.CreatedAt);
        Assert.False(notification.IsRead);
    }

    [Fact]
    public void FactoryRejectsUnsupportedNotificationTypes()
    {
        Assert.Throws<ArgumentException>(() => NotificationFactory.Create(
            new NotificationInput(
                UserId: 12,
                Type: "tournament",
                Title: "Giải đấu",
                Message: "Không thuộc phạm vi Notification hiện tại."),
            DateTime.UtcNow));
    }

    [Fact]
    public async Task RealtimeNotifierOnlyPublishesToTheTargetUser()
    {
        var notifier = new NotificationRealtimeNotifier();
        using var target = notifier.Subscribe(userId: 7);
        using var other = notifier.Subscribe(userId: 8);

        notifier.Publish(userId: 7, notificationId: 99, action: "Created");

        var targetEvent = await target.Reader.ReadAsync();
        Assert.Equal(7, targetEvent.UserId);
        Assert.Equal(99, targetEvent.NotificationId);
        Assert.Equal("Created", targetEvent.Action);
        Assert.False(other.Reader.TryRead(out _));
    }

    [Fact]
    public void NotificationModelStoresStructuredInAppMetadata()
    {
        var model = new NotificationLog
        {
            NotificationType = NotificationTypes.System,
            Title = "Thông báo hệ thống",
            Tone = NotificationTones.Default,
            LinkTo = "/notifications",
            LinkLabel = "Xem",
            CreatedAt = DateTime.UtcNow
        };

        Assert.Equal("system", model.NotificationType);
        Assert.Equal("Thông báo hệ thống", model.Title);
        Assert.Equal("default", model.Tone);
        Assert.Equal("/notifications", model.LinkTo);
        Assert.Equal("Xem", model.LinkLabel);
    }
}
