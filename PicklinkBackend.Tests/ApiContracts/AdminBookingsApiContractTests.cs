namespace PicklinkBackend.Tests;

public class AdminBookingsApiContractTests
{
    [Fact]
    public void AdminBookingsControllerExposesRealBookingList()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminBookingsController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Admin", "Implementations", "AdminBookingService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminBookingDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/bookings\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("IAdminBookingService", source);
        Assert.Contains("services.AddScoped<IAdminBookingService, AdminBookingService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.Contains("string? search", source);
        Assert.Contains("string? status", source);
        Assert.Contains("string? paymentStatus", source);
        Assert.Contains("_adminRepository.GetAdminBookingListAsync", service);
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