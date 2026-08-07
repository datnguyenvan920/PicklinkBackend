using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Tests;

public class AdminReportsApiContractTests
{
    [Fact]
    public void ReviewResultPreservesTheFullAdminReportResponse()
    {
        var response = new AdminReportResponse
        {
            CommunityReportId = 42,
            ReporterUserId = 7,
            ReporterName = "Reporter",
            ReporterEmail = "reporter@example.com",
            ReviewedAt = DateTime.UtcNow,
            ReviewedByName = "Admin"
        };

        var result = AdminReportReviewResult.Success(response);

        Assert.Same(response, result.Value);
        Assert.Equal(7, result.Value?.ReporterUserId);
        Assert.Equal("reporter@example.com", result.Value?.ReporterEmail);
        Assert.Equal("Admin", result.Value?.ReviewedByName);
    }

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
        var adminResult = File.ReadAllText(SourcePath("Services", "Admin", "AdminResult.cs"));
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
        Assert.Contains("AdminReportResponse? Value", adminResult);
        Assert.DoesNotContain("MapCommunityReport", adminResult);
        Assert.Contains("AdminResultStatus.Conflict => Conflict", source);
        Assert.Contains("public sealed class AdminReportReviewRequest", dtos);
        Assert.Contains("public class AdminReportResponse", dtos);
        Assert.DoesNotContain("Tournament", source);
    }

    [Fact]
    public void UsersCanSubmitReportsForAdminQueue()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Community", "ReportsController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Community", "Implementations", "CommunityReportSubmissionService.cs"));
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
