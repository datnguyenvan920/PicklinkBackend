namespace PicklinkBackend.Tests;

public class OwnerVenueReviewsApiContractTests
{
    [Fact]
    public void OwnerCanOnlyReadVisibleReviewsForOwnedVenues()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Owner", "OwnerVenueController.cs"));
        var contract = File.ReadAllText(SourcePath("Services", "Owner", "IOwnerVenueService.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Owner", "Implementations", "OwnerVenueService.cs"));
        var dto = File.ReadAllText(SourcePath("DTOs", "OwnerVenueDtos.cs"));

        Assert.Contains("[Authorize(Roles = \"VenueOwner\")]", controller);
        Assert.Contains("[HttpGet(\"venues/{venueId:int}/reviews\")]", controller);
        Assert.DoesNotContain("[HttpPost(\"venues/{venueId:int}/reviews\")]", controller);
        Assert.DoesNotContain("[HttpPut(\"venues/{venueId:int}/reviews\")]", controller);
        Assert.Contains("GetVenueReviews", contract);
        Assert.Contains("GetOwnedVenue(venueId", service);
        Assert.Contains("review.TargetType == \"Venue\"", service);
        Assert.Contains("!review.IsHidden", service);
        Assert.Contains("review.ModerationStatus == \"Visible\"", service);
        Assert.Contains("review.IsAnonymous ? \"Ẩn danh\"", service);
        Assert.Contains("public class OwnerVenueReviewResponse", dto);
        Assert.DoesNotContain("ModerationNote", dto.Substring(dto.IndexOf("public class OwnerVenueReviewResponse"), dto.IndexOf("public class OwnerListingFeePreviewResponse") - dto.IndexOf("public class OwnerVenueReviewResponse")));
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
