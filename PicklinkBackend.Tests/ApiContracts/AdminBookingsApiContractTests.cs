namespace PicklinkBackend.Tests;

public class AdminBookingsApiContractTests
{
    [Fact]
    public void AdminBookingsControllerExposesRealBookingList()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminBookingsController.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/bookings\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("_dbContext.Bookings", source);
        Assert.Contains("Pagination.Create", source);
        Assert.Contains("BookingCode", source);
        Assert.Contains("VenueName", source);
        Assert.Contains("OwnerEmail", source);
        Assert.Contains("PlayerEmail", source);
        Assert.Contains("PaymentStatus", source);
        Assert.DoesNotContain("Tournament", source);
    }

    [Fact]
    public void AdminBookingsSupportsSearchStatusAndPaymentFilters()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminBookingsController.cs"));

        Assert.Contains("string? search", source);
        Assert.Contains("string? status", source);
        Assert.Contains("string? paymentStatus", source);
        Assert.Contains("BookingCode.Contains", source);
        Assert.Contains("booking.Status == normalizedStatus", source);
        Assert.Contains("payment.Status == normalizedPaymentStatus", source);
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
