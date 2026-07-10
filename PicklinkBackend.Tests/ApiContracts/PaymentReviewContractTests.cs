namespace PicklinkBackend.Tests;

public class PaymentReviewContractTests
{
    [Fact]
    public void RejectingReceiptResetsRetryWindowInsteadOfKeepingReviewDeadline()
    {
        var source = File.ReadAllText(SourcePath("Services", "Payments", "PaymentService.cs"));

        Assert.Contains("ResetBookingHoldAfterPaymentRejection(payment.Booking);", source);
        Assert.Contains("GetValue(\"Booking:HoldingMinutes\", 5)", source);
        Assert.Contains("booking.HoldExpiresAt = DateTime.UtcNow.AddMinutes(retryMinutes);", source);
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
