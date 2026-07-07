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
        var queryService = File.ReadAllText(SourcePath("Services", "AdminReviewQueryService.cs"));
        var moderationService = File.ReadAllText(SourcePath("Services", "AdminReviewModerationService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminReviewDtos.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/reviews\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpPost(\"{ratingId:int}/moderate\")]", source);
        Assert.Contains("AdminReviewQueryService", source);
        Assert.Contains("AdminReviewModerationService", source);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("RatingHistories", source);
        Assert.DoesNotContain("public sealed class AdminReviewResponse", source);
        Assert.Contains("_dbContext.RatingHistories", queryService);
        Assert.Contains("Pagination.Create", queryService);
        Assert.Contains("_dbContext.RatingHistories", moderationService);
        Assert.Contains("ModerationStatus", moderationService);
        Assert.Contains("ModerationNote", moderationService);
        Assert.Contains("public sealed class AdminReviewResponse", dtos);
        Assert.Contains("public sealed class AdminReviewModerationRequest", dtos);
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
