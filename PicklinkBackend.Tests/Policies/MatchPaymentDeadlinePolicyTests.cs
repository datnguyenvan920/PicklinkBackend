using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;

namespace PicklinkBackend.Tests.Policies;

public class MatchPaymentDeadlinePolicyTests
{
    [Fact]
    public void PartialPaymentStartsOneRescueWindowThenRequiresRefund()
    {
        var booking = MatchBooking("Paid", "Pending");

        Assert.Equal(MatchPaymentDeadlineDecision.StartRescue, MatchPaymentDeadlinePolicy.Decide(booking));

        booking.Payments.ElementAt(1).StatusHistories.Add(new PaymentStatusHistory
        {
            Action = MatchPaymentDeadlinePolicy.RescueAction,
            ToStatus = "Pending"
        });

        Assert.Equal(MatchPaymentDeadlineDecision.ExpireAndRefund, MatchPaymentDeadlinePolicy.Decide(booking));
    }

    [Fact]
    public void BookingWithoutAnyCommittedPaymentExpiresImmediately()
    {
        Assert.Equal(
            MatchPaymentDeadlineDecision.Expire,
            MatchPaymentDeadlinePolicy.Decide(MatchBooking("Pending", "Pending")));
    }

    private static Booking MatchBooking(params string[] statuses) => new()
    {
        Match = new Match(),
        Payments = statuses.Select((status, index) => new Payment
        {
            PaymentId = index + 1,
            Status = status
        }).ToList()
    };
}
