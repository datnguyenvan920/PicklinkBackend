namespace PicklinkBackend.Tests;

public class AdminBookingsApiContractTests
{
    [Fact]
    public void AdminBookingsControllerExposesRealBookingList()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminBookingsController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "AdminBookingQueryService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminBookingDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/bookings\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("AdminBookingQueryService", source);
        Assert.Contains("services.AddScoped<AdminBookingQueryService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("public sealed class AdminBookingSummaryResponse", source);
        Assert.Contains("_dbContext.Bookings", service);
        Assert.Contains("Pagination.Create", service);
        Assert.Contains("BookingCode", service);
        Assert.Contains("VenueName", service);
        Assert.Contains("OwnerEmail", service);
        Assert.Contains("PlayerEmail", service);
        Assert.Contains("PaymentStatus", service);
        Assert.Contains("public sealed class AdminBookingSummaryResponse", dtos);
        Assert.DoesNotContain("Tournament", source);
    }

    [Fact]
    public void AdminBookingsSupportsSearchStatusAndPaymentFilters()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminBookingsController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "AdminBookingQueryService.cs"));

        Assert.Contains("string? search", source);
        Assert.Contains("string? status", source);
        Assert.Contains("string? paymentStatus", source);
        Assert.Contains("BookingCode.Contains", service);
        Assert.Contains("booking.Status == normalizedStatus", service);
        Assert.Contains("payment.Status == normalizedPaymentStatus", service);
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