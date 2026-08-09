namespace PicklinkBackend.Tests;

public class PlayerVenueReviewsApiContractTests
{
    [Fact]
    public void PublicVenueReviewsAreVisibleReadOnlyAndPrivacySafe()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Players", "PlayerBookingController.cs"));
        var contract = File.ReadAllText(SourcePath("Services", "Bookings", "IPlayerBookingService.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Bookings", "Implementations", "PlayerBookingService.cs"));
        var dto = File.ReadAllText(SourcePath("DTOs", "PlayerBookingDtos.cs"));

        Assert.Contains("[AllowAnonymous]", controller);
        Assert.Contains("[HttpGet(\"venues/{venueId:int}/reviews\")]", controller);
        Assert.DoesNotContain("[HttpPost(\"venues/{venueId:int}/reviews\")]", controller);
        Assert.DoesNotContain("[HttpPut(\"venues/{venueId:int}/reviews\")]", controller);
        Assert.DoesNotContain("[HttpDelete(\"venues/{venueId:int}/reviews\")]", controller);
        Assert.Contains("GetVenueReviews", contract);
        Assert.Contains("IsApprovedVenueAsync(venueId", service);
        Assert.Contains("review.TargetType == \"Venue\"", service);
        Assert.Contains("!review.IsHidden", service);
        Assert.Contains("review.ModerationStatus == \"Visible\"", service);
        Assert.Contains("review.IsAnonymous ? \"Ẩn danh\"", service);
        Assert.Contains("public class PlayerVenueReviewResponse", dto);
        Assert.DoesNotContain("ReviewerEmail", dto.Substring(dto.IndexOf("public class PlayerVenueReviewResponse"), dto.IndexOf("public class PlayerCourtAvailabilityResponse") - dto.IndexOf("public class PlayerVenueReviewResponse")));
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var fileName = relativeSegments.Last();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. relativeSegments]);
                if (File.Exists(candidate)) return candidate;

                var foundFile = Directory.GetFiles(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundFile is not null) return foundFile;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
