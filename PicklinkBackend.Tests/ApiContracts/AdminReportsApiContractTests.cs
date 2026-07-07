namespace PicklinkBackend.Tests;

public class AdminReportsApiContractTests
{
    [Fact]
    public void ReportModelAndSchemaAreRegistered()
    {
        var model = File.ReadAllText(SourcePath("Models", "CommunityReport.cs"));
        var dbContext = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var schemaStartup = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));

        Assert.Contains("public int ReporterUserId { get; set; }", model);
        Assert.Contains("public string TargetType { get; set; }", model);
        Assert.Contains("public string Status { get; set; }", model);
        Assert.Contains("DbSet<CommunityReport>", dbContext);
        Assert.Contains("COMMUNITY_REPORT", dbContext);
        Assert.Contains("EnsureCommunityReportSchema(app)", schemaStartup);
        Assert.Contains("CREATE TABLE [COMMUNITY_REPORT]", schemaStartup);
        Assert.DoesNotContain("Tournament", model);
    }

    [Fact]
    public void AdminCanListAndReviewReports()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminReportsController.cs"));
        var queryService = File.ReadAllText(SourcePath("Services", "AdminReportQueryService.cs"));
        var reviewService = File.ReadAllText(SourcePath("Services", "AdminReportReviewService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminReportDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/reports\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpPost(\"{reportId:int}/review\")]", source);
        Assert.Contains("AdminReportQueryService", source);
        Assert.Contains("AdminReportReviewService", source);
        Assert.Contains("services.AddScoped<AdminReportQueryService>()", services);
        Assert.Contains("services.AddScoped<AdminReportReviewService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("public sealed class AdminReportResponse", source);
        Assert.Contains("_dbContext.CommunityReports", queryService);
        Assert.Contains("Pagination.Create", queryService);
        Assert.Contains("ReviewedByUserId", reviewService);
        Assert.Contains("ResolutionNote", reviewService);
        Assert.Contains("public sealed class AdminReportReviewRequest", dtos);
        Assert.Contains("public sealed class AdminReportResponse", dtos);
        Assert.DoesNotContain("Tournament", source);
    }

    [Fact]
    public void UsersCanSubmitReportsForAdminQueue()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Community", "ReportsController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "CommunityReportSubmissionService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "CommunityReportDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize]", source);
        Assert.Contains("[Route(\"api/reports\")]", source);
        Assert.Contains("[HttpPost]", source);
        Assert.Contains("CommunityReportSubmissionService", source);
        Assert.Contains("services.AddScoped<CommunityReportSubmissionService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("public sealed class ReportSubmissionRequest", source);
        Assert.Contains("ReporterUserId", service);
        Assert.Contains("Status = \"Open\"", service);
        Assert.Contains("Priority = \"Normal\"", service);
        Assert.Contains("public sealed class ReportSubmissionRequest", dtos);
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