namespace PicklinkBackend.Tests;

public class AdminVenueApiContractTests
{
    [Fact]
    public void AdminVenueControllerExposesProtectedReviewEndpoints()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminVenuesController.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/venues\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpGet(\"{venueId:int}\")]", source);
        Assert.Contains("[HttpPost(\"{venueId:int}/approve\")]", source);
        Assert.Contains("[HttpPost(\"{venueId:int}/reject\")]", source);
        Assert.Contains("AdminVenueQueryService", source);
        Assert.Contains("AdminVenueApprovalService", source);
        Assert.DoesNotContain("BeginTransactionAsync", source);
        Assert.DoesNotContain("_venueRealtime.Publish", source);
        Assert.DoesNotContain("_notifications.Add(new NotificationInput", source);
        Assert.DoesNotContain("_notifications.PublishPending()", source);
    }

    [Fact]
    public void AdminVenueListSupportsSearchStatusAndPagination()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminVenuesController.cs"));
        var source = File.ReadAllText(SourcePath("Services", "AdminVenueQueryService.cs"));

        Assert.Contains("string? search", controller);
        Assert.Contains("string? status", controller);
        Assert.Contains("Pagination.NormalizePage", source);
        Assert.Contains("Pagination.NormalizePageSize", source);
        Assert.Contains("Pagination.Create", source);
        Assert.Contains("venue.VenueName.Contains(keyword)", source);
        Assert.Contains("venue.ApprovalStatus == normalizedStatus", source);
    }

    [Fact]
    public void AdminVenueDtosLiveOutsideTheController()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminVenuesController.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminVenueDtos.cs"));

        Assert.DoesNotContain("public class AdminVenueSummaryResponse", controller);
        Assert.Contains("public class AdminVenueSummaryResponse", dtos);
        Assert.Contains("public sealed class AdminVenueDetailResponse", dtos);
        Assert.Contains("public sealed class AdminVenueRejectionRequest", dtos);
    }

    [Fact]
    public void AdminVenueApprovalServiceOwnsTransactionNotificationsAndRealtime()
    {
        var service = File.ReadAllText(SourcePath("Services", "AdminVenueApprovalService.cs"));

        Assert.Contains("BeginTransactionAsync", service);
        Assert.Contains("IsolationLevel.Serializable", service);
        Assert.Contains("VenueApprovalWorkflow.Approve", service);
        Assert.Contains("VenueApprovalWorkflow.Reject", service);
        Assert.Contains("_notifications.Add(new NotificationInput", service);
        Assert.Contains("SaveChangesAsync", service);
        Assert.Contains("CommitAsync", service);
        Assert.Contains("_notifications.PublishPending()", service);
        Assert.Contains("_venueRealtime.Publish", service);
        Assert.True(
            service.IndexOf("CommitAsync", StringComparison.Ordinal)
            < service.IndexOf("_notifications.PublishPending()", StringComparison.Ordinal));
    }

    [Fact]
    public void PublicVenueListsOnlyExposeApprovedVenues()
    {
        var playerBooking = File.ReadAllText(SourcePath("Controllers", "Players", "PlayerBookingController.cs"));
        var match = File.ReadAllText(SourcePath("Controllers", "Matches", "MatchPhase8Controller.cs"));
        var nearby = File.ReadAllText(SourcePath("Controllers", "Venues", "VenueController.cs"));

        Assert.Contains("venue.ApprovalStatus == \"Approved\"", playerBooking);
        Assert.Contains("venue.ApprovalStatus == \"Approved\"", match);
        Assert.Contains("v.ApprovalStatus == \"Approved\"", nearby);
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
