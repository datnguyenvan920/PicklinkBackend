namespace PicklinkBackend.Tests;

public class AdminReportsApiContractTests
{
    [Fact]
    public void ReportModelAndSchemaAreRegistered()
    {
        var model = File.ReadAllText(SourcePath("Models", "CommunityReport.cs"));
        var dbContext = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var program = File.ReadAllText(SourcePath("Program.cs"));

        Assert.Contains("public int ReporterUserId { get; set; }", model);
        Assert.Contains("public string TargetType { get; set; }", model);
        Assert.Contains("public string Status { get; set; }", model);
        Assert.Contains("DbSet<CommunityReport>", dbContext);
        Assert.Contains("COMMUNITY_REPORT", dbContext);
        Assert.Contains("EnsureCommunityReportSchema(app)", program);
        Assert.Contains("CREATE TABLE [COMMUNITY_REPORT]", program);
        Assert.DoesNotContain("Tournament", model);
    }

    [Fact]
    public void AdminCanListAndReviewReports()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "AdminReportsController.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/reports\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpPost(\"{reportId:int}/review\")]", source);
        Assert.Contains("_dbContext.CommunityReports", source);
        Assert.Contains("Pagination.Create", source);
        Assert.Contains("ReviewedByUserId", source);
        Assert.Contains("ResolutionNote", source);
        Assert.DoesNotContain("Tournament", source);
    }

    [Fact]
    public void UsersCanSubmitReportsForAdminQueue()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "ReportsController.cs"));

        Assert.Contains("[Authorize]", source);
        Assert.Contains("[Route(\"api/reports\")]", source);
        Assert.Contains("[HttpPost]", source);
        Assert.Contains("ReporterUserId", source);
        Assert.Contains("Status = \"Open\"", source);
        Assert.Contains("Priority = \"Normal\"", source);
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
