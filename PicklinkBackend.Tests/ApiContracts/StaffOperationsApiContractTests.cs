namespace PicklinkBackend.Tests.ApiContracts;

public class StaffOperationsApiContractTests
{
    [Fact]
    public void StaffOperationsControllerDelegatesBookingOperations()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Staff", "StaffOperationsController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Staff", "StaffOperationService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "StaffOperationsDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("StaffOperationService", source);
        Assert.Contains("services.AddScoped<StaffOperationService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("public class StaffBookingResponse", source);
        Assert.DoesNotContain("private IQueryable<Booking> ScopedBookings", source);

        Assert.Contains("public class StaffBookingResponse", dtos);
        Assert.Contains("public record VerifyBookingCodeRequest", dtos);
        Assert.Contains("OperationsBookingQuery", service);
        Assert.Contains("ConfirmPaymentAsync", service);
        Assert.Contains("CheckInAsync", service);
    }

    [Fact]
    public void CheckInControllersReturnForbiddenInsteadOfMaskingItAsServerError()
    {
        var owner = File.ReadAllText(SourcePath("Controllers", "Owner", "OwnerCheckInController.cs"));
        var staff = File.ReadAllText(SourcePath("Controllers", "Staff", "StaffOperationsController.cs"));

        Assert.Contains("StaffOperationResultStatus.Forbidden", owner);
        Assert.Contains("StatusCodes.Status403Forbidden", owner);
        Assert.Contains("StaffOperationResultStatus.Forbidden", staff);
        Assert.Contains("StatusCodes.Status403Forbidden", staff);
    }

    private static string SourcePath(params string[] parts)
    {
        var cleanSegments = parts.FirstOrDefault() == "PicklinkBackend" ? parts[1..] : parts;
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

        throw new FileNotFoundException($"Could not locate {string.Join('/', parts)}.");
    }
}
