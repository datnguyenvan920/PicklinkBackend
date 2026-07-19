namespace PicklinkBackend.Tests;

public class PaymentReviewContractTests
{
    [Fact]
    public void RejectingMatchReceiptResumesThePausedHoldWindow()
    {
        var source = File.ReadAllText(SourcePath("Services", "Payments", "PaymentService.cs"));
        var booking = File.ReadAllText(SourcePath("Models", "Booking.cs"));

        Assert.Contains("ResetBookingHoldAfterPaymentRejection(payment.Booking);", source);
        Assert.Contains("public int? HoldRemainingSeconds { get; set; }", booking);
        Assert.Contains("booking.HoldRemainingSeconds = Math.Max(0, (int)Math.Floor", source);
        Assert.Contains("var remainingSeconds = booking.HoldRemainingSeconds;", source);
        Assert.Contains("booking.HoldExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(remainingSeconds.Value, 0));", source);
        Assert.Contains("booking.HoldRemainingSeconds = null;", source);
        Assert.Contains("GetValue(\"Booking:HoldingMinutes\", 5)", source);
    }
    [Fact]
    public void PlayerReadsOnlyTheUpdatedPaymentAfterRealtimeReview()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Payments", "PaymentController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Payments", "PaymentService.cs"));

        Assert.Contains("[HttpGet(\"bookings/{bookingId:int}\")]", controller);
        Assert.Contains("GetPlayerBookingPayment", controller);
        Assert.Contains("item.Booking.Player.UserId == userId.Value", service);
        Assert.Contains("item.Payer.UserId == userId.Value", service);
        Assert.Contains("Slots = payment.Booking.Slots", service);
    }


    [Fact]
    public void OperatorReviewLoadsMatchDetailsOnlyWhenThePaymentBelongsToAMatch()
    {
        var service = File.ReadAllText(SourcePath("Services", "Payments", "PaymentService.cs"));

        Assert.Contains("AuthorizedOperatorReviewQuery(int userId) => _dbContext.Payments", service);
        Assert.Contains("await LoadMatchPaymentGraphAsync(payment.Booking, cancellationToken);", service);
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
