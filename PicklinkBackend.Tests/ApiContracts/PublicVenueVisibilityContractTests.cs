using System.Text.RegularExpressions;

namespace PicklinkBackend.Tests;

public class PublicVenueVisibilityContractTests
{
    [Fact]
    public void PlayerVenueListOnlyRequiresAdminApprovalForPublicVisibility()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var getVenues = ExtractMethod(source, "GetVenues", "GetFavoriteVenues");

        Assert.Contains("venue.ApprovalStatus == \"Approved\"", getVenues);
        Assert.DoesNotContain("venue.IsOpen", getVenues);
        Assert.DoesNotContain("venue.Courts.Any(court => court.AvailabilityStatus == \"Available\")", getVenues);
    }

    [Fact]
    public void PlayerVenueAvailabilityOnlyRequiresAdminApprovalToOpenPublicSchedule()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var getAvailability = ExtractMethod(source, "GetAvailability", "CreateHolding");

        Assert.Contains("venue.ApprovalStatus == \"Approved\"", getAvailability);
        Assert.Contains(".AsSplitQuery()", getAvailability);
        Assert.DoesNotContain("&& venue.IsOpen", getAvailability);
    }

    [Fact]
    public void PlayerCanFavoriteAnyAdminApprovedPublicVenue()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var addFavoriteVenue = ExtractMethod(source, "AddFavoriteVenue", "RemoveFavoriteVenue");

        Assert.Contains("item.ApprovalStatus == \"Approved\"", addFavoriteVenue);
        Assert.DoesNotContain("&& item.IsOpen", addFavoriteVenue);
    }
    [Fact]
    public void NearbyVenueSearchOnlyRequiresAdminApprovalForPublicVisibility()
    {
        var source = File.ReadAllText(SourcePath("Services", "Venues", "VenueNearbyQueryService.cs"));

        Assert.Contains("venue.ApprovalStatus == \"Approved\"", source);
        Assert.DoesNotContain("&& venue.IsOpen", source);
    }

    private static string ExtractMethod(string source, string methodName, string nextMethodName)
    {
        var pattern = $"public .*? {methodName}\\([\\s\\S]*?\\n    public .*? {nextMethodName}\\(";
        var match = Regex.Match(source, pattern);
        Assert.True(match.Success, $"Could not locate {methodName}.");
        return match.Value;
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
