namespace PicklinkBackend.Tests;

public class AdminDashboardApiContractTests
{
    [Fact]
    public void AdminDashboardControllerExposesRealMarketplaceMetrics()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminDashboardController.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/dashboard\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("_dbContext.Users", source);
        Assert.Contains("_dbContext.Venues", source);
        Assert.Contains("_dbContext.Bookings", source);
        Assert.Contains("_dbContext.VenueListingPayments", source);
        Assert.Contains("PendingListingPaymentCount", source);
        Assert.Contains("ListingRevenueThisMonth", source);
        Assert.Contains("ExpiringListingCount", source);
        Assert.Contains("ExpiredListingCount", source);
        Assert.DoesNotContain("Tournament", source);
    }

    [Fact]
    public void AdminDashboardReturnsActionQueuesAndExpiringListingVenues()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminDashboardController.cs"));

        Assert.Contains("PendingVenueCount", source);
        Assert.Contains("PendingReview", source);
        Assert.Contains("PaidUntil", source);
        Assert.Contains("now.AddDays(7)", source);
        Assert.Contains("ActionItems", source);
        Assert.Contains("ExpiringListings", source);
        Assert.Contains("OwnerEmail", source);
        Assert.Contains("VenueName", source);
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
