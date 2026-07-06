namespace PicklinkBackend.Tests;

public class AdminVenueApiContractTests
{
    [Fact]
    public void AdminVenueControllerExposesProtectedReviewEndpoints()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "AdminVenuesController.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/venues\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpGet(\"{venueId:int}\")]", source);
        Assert.Contains("[HttpPost(\"{venueId:int}/approve\")]", source);
        Assert.Contains("[HttpPost(\"{venueId:int}/reject\")]", source);
        Assert.Contains("VenueApprovalWorkflow.Approve", source);
        Assert.Contains("VenueApprovalWorkflow.Reject", source);
        Assert.Contains("_venueRealtime.Publish", source);
        Assert.Contains("_dbContext.NotificationLogs.Add", source);
    }

    [Fact]
    public void AdminVenueListSupportsSearchStatusAndPagination()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "AdminVenuesController.cs"));

        Assert.Contains("string? search", source);
        Assert.Contains("string? status", source);
        Assert.Contains("Pagination.NormalizePage", source);
        Assert.Contains("Pagination.NormalizePageSize", source);
        Assert.Contains("Pagination.Create", source);
        Assert.Contains("venue.VenueName.Contains(keyword)", source);
        Assert.Contains("venue.ApprovalStatus == normalizedStatus", source);
    }

    [Fact]
    public void PublicVenueListsOnlyExposeApprovedVenues()
    {
        var playerBooking = File.ReadAllText(SourcePath("Controllers", "PlayerBookingController.cs"));
        var match = File.ReadAllText(SourcePath("Controllers", "MatchPhase8Controller.cs"));
        var nearby = File.ReadAllText(SourcePath("Controllers", "VenueController.cs"));

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
