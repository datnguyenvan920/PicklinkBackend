namespace PicklinkBackend.Tests;

public class NotificationApiContractTests
{
    [Fact]
    public void NotificationSchemaPersistsStructuredMetadata()
    {
        var model = File.ReadAllText(SourcePath("Models", "NotificationLog.cs"));
        var dbContext = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var schemaStartup = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));

        foreach (var property in new[]
        {
            "NotificationType",
            "Title",
            "Tone",
            "LinkTo",
            "LinkLabel",
            "CreatedAt"
        })
        {
            Assert.Contains(property, model);
            Assert.Contains(property, dbContext);
        }

        Assert.Contains("COL_LENGTH(N'NOTIFICATION_LOG', N'notificationType')", schemaStartup);
        Assert.Contains("COL_LENGTH(N'NOTIFICATION_LOG', N'createdAt')", schemaStartup);
        Assert.Contains("IX_NOTIFICATION_LOG_user_unread_created", schemaStartup);
    }

    [Fact]
    public void NotificationApiSupportsListCountReadAndDeleteOperations()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "NotificationsController.cs"));

        Assert.Contains("[Authorize]", source);
        Assert.Contains("[Route(\"api/notifications\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpGet(\"unread-count\")]", source);
        Assert.Contains("[HttpPatch(\"{notificationId:int}/read\")]", source);
        Assert.Contains("[HttpPatch(\"read-all\")]", source);
        Assert.Contains("[HttpDelete(\"{notificationId:int}\")]", source);
        Assert.Contains("[HttpDelete(\"read\")]", source);
        Assert.Contains("notification.UserId == userId.Value", source);
        Assert.Contains("Pagination.Create", source);
        Assert.Contains(".Select(notification => new NotificationResponse", source);
        Assert.DoesNotContain(".Select(notification => Map(notification))", source);
    }

    [Fact]
    public void NotificationRealtimeStreamIsAuthenticatedAndUserScoped()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "NotificationRealtimeController.cs"));

        Assert.Contains("[Authorize]", source);
        Assert.DoesNotContain("[AllowAnonymous]", source);
        Assert.Contains("_notifier.Subscribe(userId.Value)", source);
        Assert.Contains("event: notification-updated", source);
    }

    [Fact]
    public void NotificationFeatureDoesNotIntroduceTournamentEvents()
    {
        var factory = File.ReadAllText(SourcePath("Services", "NotificationFactory.cs"));
        var controller = File.ReadAllText(SourcePath("Controllers", "NotificationsController.cs"));

        Assert.DoesNotContain("Tournament", factory);
        Assert.DoesNotContain("tournament", factory, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tournament", controller, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotificationSourcesUseServiceAndPublishPendingEvents()
    {
        var service = File.ReadAllText(SourcePath("Services", "NotificationService.cs"));
        var matchRoot = File.ReadAllText(SourcePath("Controllers", "MatchController.cs"));
        var adminVenues = File.ReadAllText(SourcePath("Controllers", "AdminVenuesController.cs"));
        var matches = File.ReadAllText(SourcePath("Controllers", "MatchRecommendationsController.cs"));
        var community = File.ReadAllText(SourcePath("Controllers", "CommunityController.cs"));
        var payments = File.ReadAllText(SourcePath("Controllers", "PaymentController.cs"));

        Assert.Contains("PublishPending", service);
        Assert.Contains("NotificationService", matchRoot);

        foreach (var source in new[] { adminVenues, matches, community, payments })
        {
            Assert.Contains("_notifications.Add(new NotificationInput", source);
            Assert.Contains("_notifications.PublishPending()", source);
            Assert.DoesNotContain("NotificationLogs.Add(new NotificationLog", source);
            Assert.DoesNotContain("Tournament", source);
        }

        Assert.Contains("NotificationTypes.Payment", payments);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
