using System.Text.RegularExpressions;

namespace PicklinkBackend.Tests;

public class PublicVenueVisibilityContractTests
{
    [Fact]
    public void PlayerVenueListOnlyRequiresAdminApprovalForPublicVisibility()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var getVenues = ExtractMethod(source, "GetVenues", "GetFavoriteVenues");

        Assert.Contains("_venueRepository.GetApprovedVenuesQueryable()", getVenues);
    }

    [Fact]
    public void PlayerVenueAvailabilityOnlyRequiresAdminApprovalToOpenPublicSchedule()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var getAvailability = ExtractMethod(source, "GetAvailability", "CreateHolding");

        Assert.Contains("_venueRepository.GetApprovedVenueForAvailabilityAsync", getAvailability);
    }

    [Fact]
    public void PlayerCanFavoriteAnyAdminApprovedPublicVenue()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var addFavoriteVenue = ExtractMethod(source, "AddFavoriteVenue", "RemoveFavoriteVenue");

        Assert.Contains("_venueRepository.IsApprovedVenueAsync", addFavoriteVenue);
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
        var cleanSegments = relativeSegments.FirstOrDefault() == "PicklinkBackend" ? relativeSegments[1..] : relativeSegments;
        var fileName = cleanSegments.Last();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. cleanSegments]);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;

                var foundFile = Directory.GetFiles(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundFile is not null) return foundFile;

                var foundDir = Directory.GetDirectories(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundDir is not null) return foundDir;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
