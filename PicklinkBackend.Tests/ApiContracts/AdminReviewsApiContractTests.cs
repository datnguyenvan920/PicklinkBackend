namespace PicklinkBackend.Tests;

public class AdminReviewsApiContractTests
{
    [Fact]
    public void RatingHistorySupportsAdminModerationFields()
    {
        var model = File.ReadAllText(SourcePath("Models", "RatingHistory.cs"));
        var dbContext = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var schemaStartup = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));

        Assert.Contains("public bool IsHidden { get; set; }", model);
        Assert.Contains("public string ModerationStatus { get; set; }", model);
        Assert.Contains("public string? ModerationNote { get; set; }", model);
        Assert.Contains("public int? ModeratedByUserId { get; set; }", model);
        Assert.Contains("moderationStatus", dbContext);
        Assert.Contains("isHidden", dbContext);
        Assert.Contains("EnsureAdminReviewSchema(app)", schemaStartup);
        Assert.Contains("COL_LENGTH(N'RATING_HISTORY', N'isHidden')", schemaStartup);
    }

    [Fact]
    public void AdminCanListAndModerateReviews()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminReviewsController.cs"));
        var reviewService = File.ReadAllText(SourcePath("Services", "Admin", "Implementations", "AdminReviewService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminReviewDtos.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/reviews\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpPost(\"{ratingId:int}/moderate\")]", source);
        Assert.Contains("IAdminReviewService", source);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.Contains("_adminRepository.GetAdminReviewListAsync", reviewService);
        Assert.Contains("Pagination.Create", reviewService);
        Assert.Contains("ModerationStatus", reviewService);
        Assert.Contains("ModerationNote", reviewService);
        Assert.Contains("review.IsHidden = normalizedStatus == \"Hidden\"", reviewService);
        Assert.Contains("AdminResultStatus.Conflict => Conflict", source);
        Assert.Contains("public class AdminReviewResponse", dtos);
        Assert.Contains("public sealed class AdminReviewModerationRequest", dtos);
        Assert.DoesNotContain("Tournament", source);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var cleanSegments = relativeSegments.FirstOrDefault() == "PicklinkBackend" ? relativeSegments[1..] : relativeSegments;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. cleanSegments]);
                if (File.Exists(candidate)) return candidate;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
