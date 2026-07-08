using PicklinkBackend.Models;
using PicklinkBackend.Services.Notifications;

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
                Title: "  LÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi thi ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥u  ",
                Message: "  Minh mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi bÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡n tham gia trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥u.  ",
                Tone: NotificationTones.Urgent,
                LinkTo: "/matches/45",
                LinkLabel: "Xem trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n"),
            now);

        Assert.Equal(12, notification.UserId);
        Assert.Equal("match", notification.NotificationType);
        Assert.Equal("LÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi thi ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥u", notification.Title);
        Assert.Equal("Minh mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi bÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡n tham gia trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥u.", notification.Message);
        Assert.Equal("urgent", notification.Tone);
        Assert.Equal("/matches/45", notification.LinkTo);
        Assert.Equal("Xem trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n", notification.LinkLabel);
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
                Title: "GiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥u",
                Message: "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng thuÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢c phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡m vi Notification hiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡n tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡i."),
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
            Title = "ThÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng bÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡o hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡ thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ng",
            Tone = NotificationTones.Default,
            LinkTo = "/notifications",
            LinkLabel = "Xem",
            CreatedAt = DateTime.UtcNow
        };

        Assert.Equal("system", model.NotificationType);
        Assert.Equal("ThÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng bÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡o hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡ thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ng", model.Title);
        Assert.Equal("default", model.Tone);
        Assert.Equal("/notifications", model.LinkTo);
        Assert.Equal("Xem", model.LinkLabel);
    }
}
