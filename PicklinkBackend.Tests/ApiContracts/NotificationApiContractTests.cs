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
        var source = File.ReadAllText(SourcePath("Controllers", "Notifications", "NotificationsController.cs"));
        var queryService = File.ReadAllText(SourcePath("Services", "Notifications", "NotificationQueryService.cs"));
        var commandService = File.ReadAllText(SourcePath("Services", "Notifications", "NotificationCommandService.cs"));

        Assert.Contains("[Authorize]", source);
        Assert.Contains("[Route(\"api/notifications\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpGet(\"unread-count\")]", source);
        Assert.Contains("[HttpPatch(\"{notificationId:int}/read\")]", source);
        Assert.Contains("[HttpPatch(\"read-all\")]", source);
        Assert.Contains("[HttpDelete(\"{notificationId:int}\")]", source);
        Assert.Contains("[HttpDelete(\"read\")]", source);
        Assert.Contains("NotificationQueryService", source);
        Assert.Contains("NotificationCommandService", source);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("NotificationLogs", source);
        Assert.DoesNotContain("public sealed class NotificationResponse", source);
        Assert.Contains("notification.UserId == userId", queryService);
        Assert.Contains("Pagination.Create", queryService);
        Assert.Contains(".Select(notification => new NotificationResponse", queryService);
        Assert.Contains("notification.UserId == userId", commandService);
        Assert.Contains("_notifications.PublishChanged", commandService);
        Assert.DoesNotContain(".Select(notification => Map(notification))", queryService);
    }

    [Fact]
    public void NotificationDtosLiveOutsideTheController()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Notifications", "NotificationsController.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "NotificationDtos.cs"));

        Assert.DoesNotContain("public sealed class NotificationResponse", controller);
        Assert.Contains("public sealed class NotificationResponse", dtos);
        Assert.Contains("public sealed class NotificationUnreadCountResponse", dtos);
    }

    [Fact]
    public void NotificationRealtimeStreamIsAuthenticatedAndUserScoped()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Realtime", "NotificationRealtimeController.cs"));

        Assert.Contains("[Authorize]", source);
        Assert.DoesNotContain("[AllowAnonymous]", source);
        Assert.Contains("_notifier.Subscribe(userId.Value)", source);
        Assert.Contains("event: notification-updated", source);
    }

    [Fact]
    public void NotificationFeatureDoesNotIntroduceTournamentEvents()
    {
        var factory = File.ReadAllText(SourcePath("Services", "Notifications", "NotificationFactory.cs"));
        var controller = File.ReadAllText(SourcePath("Controllers", "Notifications", "NotificationsController.cs"));

        Assert.DoesNotContain("Tournament", factory);
        Assert.DoesNotContain("tournament", factory, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tournament", controller, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotificationSourcesUseServiceAndPublishPendingEvents()
    {
        var service = File.ReadAllText(SourcePath("Services", "Notifications", "NotificationService.cs"));
        var matchRoot = File.ReadAllText(SourcePath("Services", "Matches", "MatchService.cs"));
        var adminVenues = File.ReadAllText(SourcePath("Services", "Admin", "AdminVenueApprovalService.cs"));
        var matches = File.ReadAllText(SourcePath("Services", "Matches", "MatchService.Recommendations.cs"));
        var community = ReadSourceGroup(SourceDirectory("Services"), "CommunityService*.cs");
        var payments = File.ReadAllText(SourcePath("Services", "Payments", "PaymentService.cs"));

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

    private static string SourceDirectory(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }

    private static string ReadSourceGroup(string directory, string searchPattern)
    {
        return string.Join(
            Environment.NewLine,
            Directory
                .GetFiles(directory, searchPattern, SearchOption.AllDirectories)
                .Select(File.ReadAllText));
    }
}
