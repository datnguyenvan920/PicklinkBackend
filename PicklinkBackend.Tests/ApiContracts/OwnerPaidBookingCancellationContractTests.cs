namespace PicklinkBackend.Tests;

public class OwnerPaidBookingCancellationContractTests
{
    [Fact]
    public void OwnerCannotCancelPaidBookingWithoutRefundWorkflow()
    {
        var allSource = File.ReadAllText(SourcePath("Services", "Owner", "OwnerVenueService.cs"));
        var start = allSource.IndexOf("public async Task<ServiceResult> UpdateBookingStatus", StringComparison.Ordinal);
        var end = allSource.IndexOf("private static bool HasStartedSlot", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var source = allSource[start..end];

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)", source);
        Assert.Contains("request.Status == \"Cancelled\" && booking.Payments.Any(payment => payment.Status == \"Paid\")", source);
        Assert.Contains("Booking đã thanh toán không thể hủy khi chưa có quy trình hoàn tiền.", source);
        Assert.Contains("CanCancel = !paidBookingIds.Contains(booking.BookingId) && !HasStartedSlot", allSource);
        Assert.True(source.IndexOf("transaction.CommitAsync(cancellationToken)", StringComparison.Ordinal)
            < source.IndexOf("_scheduleRealtime.Publish(new ScheduleChangedEvent", StringComparison.Ordinal));
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
