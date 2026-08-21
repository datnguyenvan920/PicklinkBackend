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
        Assert.Contains("_bookingRepository.Bookings", service);
        Assert.Contains("OwnerRevenueReportResponse", service);
        Assert.Contains("RefundedAmount = bookings.SelectMany", service);
        Assert.Contains("RefundAmount = booking.Payments.Where", service);
        Assert.Contains("RefundPending", service);
        Assert.Contains("Refunded", service);
        Assert.Contains("public class OwnerBookingResponse", dtos);
        Assert.Contains("public class OwnerRevenueReportResponse", dtos);
        Assert.Contains("public decimal RefundedAmount", dtos);
        Assert.Contains("public decimal RefundAmount", dtos);
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
    public void OwnerBookingListsRequireRecordedPaymentActivity()
    {
        var service = File.ReadAllText(SourcePath("Services", "Owner", "OwnerOperationQueryService.cs"));

        Assert.Contains("item.MatchId == null &&", service);
        Assert.Contains("item.MatchId != null &&", service);
        Assert.Contains("item.Payments.Any(payment => payment.SubmittedAt != null", service);
        Assert.Contains("|| payment.PaidAt != null", service);
        Assert.Contains("|| payment.Status == \"Paid\"", service);
        Assert.Contains("|| payment.Status == \"WaitingForConfirmation\"", service);
        Assert.Contains("|| payment.Status == \"RefundPending\"", service);
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
