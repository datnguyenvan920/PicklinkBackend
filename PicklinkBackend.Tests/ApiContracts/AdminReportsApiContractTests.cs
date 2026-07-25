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
        var reportService = File.ReadAllText(SourcePath("Services", "Admin", "Implementations", "AdminReportService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminReportDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/reports\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpPost(\"{reportId:int}/review\")]", source);
        Assert.Contains("IAdminReportService", source);
        Assert.Contains("services.AddScoped<IAdminReportService, AdminReportService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.Contains("_adminRepository.GetAdminReportListAsync", reportService);
        Assert.Contains("Pagination.Create", reportService);
        Assert.Contains("ReviewedByUserId", reportService);
        Assert.Contains("ResolutionNote", reportService);
        Assert.Contains("public sealed class AdminReportReviewRequest", dtos);
        Assert.Contains("public class AdminReportResponse", dtos);
        Assert.DoesNotContain("Tournament", source);
    }

    [Fact]
    public void UsersCanSubmitReportsForAdminQueue()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Community", "ReportsController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Community", "CommunityReportSubmissionService.cs"));
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