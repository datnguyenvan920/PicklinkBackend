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
        var source = File.ReadAllText(SourcePath("Controllers", "AdminReviewsController.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/reviews\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpPost(\"{ratingId:int}/moderate\")]", source);
        Assert.Contains("_dbContext.RatingHistories", source);
        Assert.Contains("Pagination.Create", source);
        Assert.Contains("IsHidden", source);
        Assert.Contains("ModerationStatus", source);
        Assert.Contains("ModerationNote", source);
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
