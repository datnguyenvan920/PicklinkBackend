namespace PicklinkBackend.Tests;

public class OwnerOperationsApiContractTests
{
    [Fact]
    public void OwnerOperationsControllerDelegatesBookingAndRevenueQueries()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Owner", "OwnerOperationsController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Owner", "OwnerOperationQueryService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "OwnerOperationsDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize(Roles = \"VenueOwner\")]", source);
        Assert.Contains("[Route(\"api/owner\")]", source);
        Assert.Contains("[HttpGet(\"bookings\")]", source);
        Assert.Contains("[HttpGet(\"bookings/{bookingId:int}\")]", source);
        Assert.Contains("[HttpGet(\"reports/revenue\")]", source);
        Assert.Contains("OwnerOperationQueryService", source);
        Assert.Contains("services.AddScoped<OwnerOperationQueryService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("public class OwnerBookingResponse", source);
        Assert.Contains("_dbContext.Bookings", service);
        Assert.Contains("BookingQuery", service);
        Assert.Contains("OwnerRevenueReportResponse", service);
        Assert.Contains("public class OwnerBookingResponse", dtos);
        Assert.Contains("public class OwnerRevenueReportResponse", dtos);
    }

    [Fact]
    public void OwnerBookingDateFilterUsesVietnamBookingCreatedDate()
    {
        var service = File.ReadAllText(SourcePath("Services", "Owner", "OwnerOperationQueryService.cs"));

        Assert.Contains("VietnamTime.ToUtc(from.Value.ToDateTime(TimeOnly.MinValue))", service);
        Assert.Contains("item.CreatedAt >= start", service);
        Assert.Contains("item.CreatedAt < end", service);
        Assert.Contains("query.OrderByDescending(item => item.CreatedAt)", service);
    }

    [Fact]
    public void OwnerRegularBookingsRequireASubmittedPaymentReceipt()
    {
        var service = File.ReadAllText(SourcePath("Services", "Owner", "OwnerOperationQueryService.cs"));

        Assert.Contains("item.MatchId == null &&", service);
        Assert.Contains("item.Payments.Any(payment => payment.SubmittedAt != null)", service);
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