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
        Assert.Contains("_bookingRepository", service);
        Assert.Contains("BookingStatus = Completed", service);
        Assert.Contains("CheckInStatus = CheckedIn", service);
        Assert.Contains("OverallRating", service);
        Assert.Contains("public class CreateBookingReviewRequest", dtos);
        Assert.Contains("public class BookingReviewResponse", dtos);
        Assert.DoesNotContain("Tournament", source);
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