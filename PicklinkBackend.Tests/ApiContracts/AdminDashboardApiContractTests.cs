namespace PicklinkBackend.Tests;

public class AdminDashboardApiContractTests
{
    [Fact]
    public void AdminDashboardControllerExposesRealMarketplaceMetrics()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminDashboardController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Admin", "Implementations", "AdminDashboardService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminDashboardDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/dashboard\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("IAdminDashboardService", source);
        Assert.Contains("services.AddScoped<IAdminDashboardService, AdminDashboardService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.Contains("_adminRepository.GetAdminDashboardAsync", service);
        Assert.Contains("public sealed class AdminDashboardResponse", dtos);
    }

    [Fact]
    public void AdminDashboardReturnsActionQueuesAndExpiringListingVenues()
    {
        var source = File.ReadAllText(SourcePath("Repositories", "AdminRepository.cs"));

        Assert.Contains("PendingVenueCount", source);
        Assert.Contains("PendingReview", source);
        Assert.Contains("PaidUntil", source);
        Assert.Contains("ActionItems", source);
        Assert.Contains("ExpiringListings", source);
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