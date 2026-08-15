using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Bookings;

public enum MatchPaymentDeadlineDecision
{
    Expire,
    StartRescue,
    ExpireAndRefund
}

public static class MatchPaymentDeadlinePolicy
// ponytail: payment history is the rescue marker; no extra booking-state column is needed.
{
    public const string RescueAction = "MatchPaymentRescueStarted";

    public static MatchPaymentDeadlineDecision Decide(Booking booking)
    {
        if (booking.Match is null || !booking.Payments.Any(payment => payment.Status == "Pending"))
            return MatchPaymentDeadlineDecision.Expire;

        var hasCommittedPayment = booking.Payments.Any(payment => payment.Status is "Paid" or "WaitingForConfirmation");
        var rescueStarted = booking.Payments
            .SelectMany(payment => payment.StatusHistories)
            .Any(history => history.Action == RescueAction);

        if (!rescueStarted)
            return hasCommittedPayment ? MatchPaymentDeadlineDecision.StartRescue : MatchPaymentDeadlineDecision.Expire;

        return hasCommittedPayment
            ? MatchPaymentDeadlineDecision.ExpireAndRefund
            : MatchPaymentDeadlineDecision.Expire;
    }
}
