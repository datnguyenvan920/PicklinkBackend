namespace PicklinkBackend.Tests.ApiContracts;

public class StaffOperationsApiContractTests
{
    [Fact]
    public void StaffOperationsControllerDelegatesBookingOperations()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Staff", "StaffOperationsController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "StaffOperationService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "StaffOperationsDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("StaffOperationService", source);
        Assert.Contains("services.AddScoped<StaffOperationService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("public class StaffBookingResponse", source);
        Assert.DoesNotContain("private IQueryable<Booking> ScopedBookings", source);

        Assert.Contains("public class StaffBookingResponse", dtos);
        Assert.Contains("public record VerifyBookingCodeRequest", dtos);
        Assert.Contains("private IQueryable<Booking> ScopedBookings", service);
        Assert.Contains("ConfirmAtCourtPaymentAsync", service);
        Assert.Contains("CheckInMatchParticipantAsync", service);
    }

    private static string SourcePath(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PicklinkBackend"));
        return Path.Combine(new[] { root }.Concat(parts).ToArray());
    }
}
