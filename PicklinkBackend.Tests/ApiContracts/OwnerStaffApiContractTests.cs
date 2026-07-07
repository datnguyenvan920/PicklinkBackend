namespace PicklinkBackend.Tests;

public class OwnerStaffApiContractTests
{
    [Fact]
    public void OwnerStaffControllerDelegatesStaffManagementWorkflow()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Owner", "OwnerStaffController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "OwnerStaffService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "OwnerStaffDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize(Roles = \"VenueOwner\")]", source);
        Assert.Contains("[Route(\"api/owner/staff\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpPost]", source);
        Assert.Contains("[HttpPost(\"accounts\")]", source);
        Assert.Contains("[HttpPatch(\"{staffId:int}\")]", source);
        Assert.Contains("[HttpGet(\"check-in-history\")]", source);
        Assert.Contains("OwnerStaffService", source);
        Assert.Contains("services.AddScoped<OwnerStaffService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("IPasswordHasher", source);
        Assert.DoesNotContain("public class OwnerStaffResponse", source);
        Assert.Contains("_dbContext.Staff", service);
        Assert.Contains("_passwordHasher.Hash", service);
        Assert.Contains("AllowedPermissions", service);
        Assert.Contains("VenueAuditLogs.Add", service);
        Assert.Contains("public record AssignStaffRequest", dtos);
        Assert.Contains("public class OwnerCheckInHistoryResponse", dtos);
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