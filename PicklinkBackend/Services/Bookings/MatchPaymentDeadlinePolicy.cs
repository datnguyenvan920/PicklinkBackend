using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Bookings;

public enum MatchPaymentDeadlineDecision
{
    Expire,
    ExpireAndRefund
}

public static class MatchPaymentDeadlinePolicy
{
    public static MatchPaymentDeadlineDecision Decide(Booking booking)
    {
        if (booking.Match is null || !booking.Payments.Any(payment => payment.Status == "Pending"))
            return MatchPaymentDeadlineDecision.Expire;

        var hasCommittedPayment = booking.Payments.Any(payment => payment.Status is "Paid" or "WaitingForConfirmation");
        return hasCommittedPayment
            ? MatchPaymentDeadlineDecision.ExpireAndRefund
            : MatchPaymentDeadlineDecision.Expire;
    }
}
