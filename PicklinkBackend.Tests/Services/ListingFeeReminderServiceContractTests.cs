namespace PicklinkBackend.Tests;

public class ListingFeeReminderServiceContractTests
{
    [Fact]
    public void ListingFeeReminderServiceWarnsOwnersBeforePaidUntilExpires()
    {
        var source = File.ReadAllText(SourcePath("Services", "ListingFeeReminderService.cs"));

        Assert.Contains("BackgroundService", source);
        Assert.Contains("IsListingFeeSchemaReadyAsync", source);
        Assert.Contains("OBJECT_ID(N'[VENUE_LISTING_PAYMENT]', N'U')", source);
        Assert.Contains("VenueListingPayments", source);
        Assert.Contains("PaidUntil", source);
        Assert.Contains("now.AddDays(7)", source);
        Assert.Contains("NotificationTypes.Court", source);
        Assert.Contains("NotificationTones.Urgent", source);
        Assert.Contains("phi len san", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotificationLogs", source);
        Assert.Contains("CreatedAt >= todayStart", source);
        Assert.Contains("PublishCreated", source);
        Assert.DoesNotContain("Tournament", source);
    }

    [Fact]
    public void ListingFeeReminderServiceIsRegistered()
    {
        var serviceRegistration = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("AddHostedService<ListingFeeReminderService>", serviceRegistration);
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
