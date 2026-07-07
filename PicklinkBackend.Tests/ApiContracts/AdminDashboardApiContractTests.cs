namespace PicklinkBackend.Tests;

public class AdminDashboardApiContractTests
{
    [Fact]
    public void AdminDashboardControllerExposesRealMarketplaceMetrics()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminDashboardController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "AdminDashboardService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminDashboardDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/dashboard\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("AdminDashboardService", source);
        Assert.Contains("services.AddScoped<AdminDashboardService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("public sealed class AdminDashboardResponse", source);
        Assert.Contains("_dbContext.Users", service);
        Assert.Contains("_dbContext.Venues", service);
        Assert.Contains("_dbContext.Bookings", service);
        Assert.Contains("_dbContext.VenueListingPayments", service);
        Assert.Contains("PendingListingPaymentCount", service);
        Assert.Contains("ListingRevenueThisMonth", service);
        Assert.Contains("ExpiringListingCount", service);
        Assert.Contains("ExpiredListingCount", service);
        Assert.Contains("public sealed class AdminDashboardResponse", dtos);
        Assert.DoesNotContain("Tournament", source);
    }

    [Fact]
    public void AdminDashboardReturnsActionQueuesAndExpiringListingVenues()
    {
        var source = File.ReadAllText(SourcePath("Services", "AdminDashboardService.cs"));

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