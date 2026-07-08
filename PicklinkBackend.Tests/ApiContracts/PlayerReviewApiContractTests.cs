namespace PicklinkBackend.Tests;

public class PlayerReviewApiContractTests
{
    [Fact]
    public void PlayerReviewControllerDelegatesBookingReviewWorkflow()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Players", "PlayerReviewController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingReviewService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "PlayerBookingDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize]", source);
        Assert.Contains("[Route(\"api/player-reviews\")]", source);
        Assert.Contains("[HttpGet(\"booking/{bookingId:int}\")]", source);
        Assert.Contains("[HttpPost(\"booking/{bookingId:int}\")]", source);
        Assert.Contains("PlayerBookingReviewService", source);
        Assert.Contains("services.AddScoped<PlayerBookingReviewService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.Contains("_dbContext.RatingHistories", service);
        Assert.Contains("BookingStatus = Completed", service);
        Assert.Contains("CheckInStatus = CheckedIn", service);
        Assert.Contains("OverallRating", service);
        Assert.Contains("public class CreateBookingReviewRequest", dtos);
        Assert.Contains("public class BookingReviewResponse", dtos);
        Assert.DoesNotContain("Tournament", source);
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