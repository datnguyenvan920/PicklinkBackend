using System.Text.RegularExpressions;

namespace PicklinkBackend.Tests;

public class PlayerBookingPaymentGroupPolicyTests
{
    [Fact]
    public void PaymentGroupSubmissionAndApprovalCoverEveryBookingPayment()
    {
        var source = File.ReadAllText(SourcePath("Services", "Payments", "PaymentService.cs"));
        var approve = ExtractMethod(source, "ApprovePayment", "RejectPayment");

        Assert.Contains("SubmitPlayerBookingGroupTransfer", source);
        Assert.Contains("item.PaymentGroupId == payment.PaymentGroupId", approve);
        Assert.DoesNotContain("item.BookingId == payment.BookingId", approve);
        Assert.Contains("FinalizeBookingAfterPaymentApproval(groupPayment)", approve);
        Assert.Contains(".Select(item => item.Booking)", approve);
        Assert.Contains(".DistinctBy(item => item.BookingId)", approve);
        Assert.Contains("confirmedBooking.StatusHistories.Add(new BookingStatusHistory", approve);
        Assert.DoesNotContain("payment.Booking.MatchId.HasValue && payment.Booking.Status == \"Confirmed\") payment.Booking.StatusHistories.Add", approve);
    }

    private static string ExtractMethod(string source, string methodName, string nextMethodName)
    {
        var match = Regex.Match(source, $"public .*? {methodName}\\([\\s\\S]*?\\n    public .*? {nextMethodName}\\(");
        Assert.True(match.Success, $"Could not locate {methodName}.");
        return match.Value;
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException();
    }
}
