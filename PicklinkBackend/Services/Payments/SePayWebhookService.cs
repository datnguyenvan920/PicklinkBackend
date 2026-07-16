using System.Data;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Matches;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Schedules;

namespace PicklinkBackend.Services.Payments;

public sealed class SePayWebhookService
{
    private readonly ApplicationDbContext _db;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly PaymentRealtimeNotifier _paymentRealtime;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private readonly NotificationService _notifications;

    public SePayWebhookService(ApplicationDbContext db, ScheduleRealtimeNotifier scheduleRealtime,
        PaymentRealtimeNotifier paymentRealtime, MatchRealtimeNotifier matchRealtime,
        NotificationService notifications)
    {
        _db = db;
        _scheduleRealtime = scheduleRealtime;
        _paymentRealtime = paymentRealtime;
        _matchRealtime = matchRealtime;
        _notifications = notifications;
    }

    public async Task<SePayWebhookResult> Process(SePayWebhookRequest request, CancellationToken cancellationToken)
    {
        if (request.Id <= 0 || !request.TransferType.Equals("in", StringComparison.OrdinalIgnoreCase)
            || request.TransferAmount <= 0 || string.IsNullOrWhiteSpace(request.AccountNumber))
            return Fail(400, "Invalid incoming transaction.");

        var code = request.Code?.Trim().ToUpperInvariant();
        var content = request.Content.Trim();
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(content))
            return Fail(400, "Payment code is required.");

        var paymentCodes = Regex.Matches(content.ToUpperInvariant(), @"PLG-[A-Z0-9]{16}")
            .Select(match => match.Value)
            .Append(code)
            .Append(content.ToUpperInvariant())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .ToArray();

        // Resolve the payment with equality before opening a transaction. A column-side Contains scan can
        // exhaust SQL Server's memory grant, leaving SePay waiting until its 30-second timeout.
        var candidate = await _db.Payments.AsNoTracking()
            .Where(item => item.BankAccountNumber == request.AccountNumber.Trim()
                && item.TransferContent != null
                && paymentCodes.Contains(item.TransferContent))
            .Select(item => new { item.PaymentId, item.PaymentGroupId })
            .FirstOrDefaultAsync(cancellationToken);
        if (candidate is null) return Fail(404, "No payment matches this account and payment code.");

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"sepay-transaction:{request.Id}", cancellationToken))
            return Fail(409, "Transaction is being processed.");
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"payment-review:{candidate.PaymentId}", cancellationToken))
            return Fail(409, "Payment is being processed.");

        var groupPayments = await _db.Payments
            .Include(item => item.StatusHistories)
            .Include(item => item.Payer).ThenInclude(item => item.User)
            .Where(item => candidate.PaymentGroupId.HasValue
                ? item.PaymentGroupId == candidate.PaymentGroupId
                : item.PaymentId == candidate.PaymentId)
            .OrderBy(item => item.PaymentId)
            .ToListAsync(cancellationToken);
        if (groupPayments.Count == 0) return Fail(404, "Payment not found.");

        var bookingIds = groupPayments.Select(item => item.BookingId).Distinct().ToArray();
        var bookings = await _db.Bookings
            .Include(item => item.StatusHistories)
            .Include(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner)
            .Where(item => bookingIds.Contains(item.BookingId))
            .ToListAsync(cancellationToken);
        await _db.Payments.Where(item => bookingIds.Contains(item.BookingId)).LoadAsync(cancellationToken);

        var matchIds = bookings.Where(item => item.MatchId.HasValue)
            .Select(item => item.MatchId!.Value).Distinct().ToArray();
        if (matchIds.Length > 0)
            await _db.Matches.Include(item => item.MatchParticipants)
                .Where(item => matchIds.Contains(item.MatchId)).LoadAsync(cancellationToken);
        await _db.BookingSlots.Include(item => item.Court)
            .Where(item => bookingIds.Contains(item.BookingId)).LoadAsync(cancellationToken);

        var payment = groupPayments[0];
        if (groupPayments.All(item => item.Status == "Paid")) return Success();
        if (groupPayments.Any(item => item.Status is not "Pending" and not "WaitingForConfirmation"))
            return Fail(409, "Payment is not eligible for automatic confirmation.");

        var expectedAmount = decimal.Round(groupPayments.Sum(item => (decimal)item.Amount), 0, MidpointRounding.AwayFromZero);
        if (expectedAmount != request.TransferAmount) return Fail(400, $"Amount mismatch. Expected {expectedAmount:0} VND.");
        if (groupPayments.Any(item => item.Booking.Status != "Holding" || item.Booking.HoldExpiresAt <= DateTime.UtcNow))
            return Fail(409, "Booking hold has expired.");
        if (payment.Booking.MatchId is int matchId
            && !await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Fail(409, "Match is being updated.");

        var now = DateTime.UtcNow;
        var reference = string.IsNullOrWhiteSpace(request.ReferenceCode) ? request.Id.ToString() : request.ReferenceCode.Trim();
        foreach (var item in groupPayments) ConfirmPayment(item, now, reference);
        foreach (var booking in groupPayments.Select(item => item.Booking).Distinct()) FinalizeBooking(booking, now, reference);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishUpdates(groupPayments);
        return Success();
    }

    private void ConfirmPayment(Payment payment, DateTime now, string reference)
    {
        var previous = payment.Status;
        payment.Status = "Paid";
        payment.PaidAt = now;
        payment.VerifiedAt = now;
        payment.VerifiedByUserId = null;
        payment.RejectionReason = null;
        payment.StatusHistories.Add(new PaymentStatusHistory
        {
            FromStatus = previous, ToStatus = "Paid", Action = "SePayAutoConfirmed",
            Reason = $"SePay transaction {reference}", CreatedAt = now
        });
        _db.VenueAuditLogs.Add(new VenueAuditLog
        {
            VenueId = payment.Booking.Court.VenueId,
            ActorId = payment.Booking.Court.Venue.Owner.UserId,
            Action = $"PaymentAutoConfirmed:{payment.PaymentId}:SePay:{reference}", Timestamp = now
        });
        _notifications.Add(new NotificationInput(
            payment.Payer.UserId, NotificationTypes.Payment, "Thanh toán đã được xác nhận",
            $"Thanh toán cho booking {payment.Booking.BookingCode ?? $"PL-{payment.BookingId}"} đã được SePay xác nhận.",
            NotificationTones.Success, "/my-bookings", "Xem đặt sân"));
    }

    private static void FinalizeBooking(Booking booking, DateTime now, string reference)
    {
        if (booking.Match is null) booking.Status = "Confirmed";
        else
        {
            var accepted = booking.Match.MatchParticipants.Where(item => item.Status is "Approved" or "Accepted")
                .Select(item => item.PlayerId).ToHashSet();
            var paid = booking.Payments.Where(item => item.Status == "Paid").Select(item => item.PayerId).ToHashSet();
            var canConfirm = accepted.Count == booking.Match.RequiredPlayerCount && accepted.All(paid.Contains);
            booking.Match.Status = canConfirm ? "Booked" : "BookingPending";
            booking.Status = canConfirm ? "Confirmed" : "Holding";
        }

        if (booking.Status != "Confirmed") return;
        booking.HoldExpiresAt = null;
        booking.StatusHistories.Add(new BookingStatusHistory
        {
            FromStatus = "Holding", ToStatus = "Confirmed",
            Reason = $"SePay tự động xác nhận giao dịch {reference}", ChangedAt = now
        });
    }

    private void PublishUpdates(IReadOnlyCollection<Payment> payments)
    {
        foreach (var payment in payments)
        {
            _paymentRealtime.Publish(new PaymentChangedEvent(payment.PaymentId, payment.BookingId,
                payment.Booking.Court.VenueId, payment.Status, "AutoConfirmed"));
            if (payment.Booking.MatchId.HasValue) _matchRealtime.Publish(payment.Booking.MatchId.Value, "PaymentAutoConfirmed");
        }
        foreach (var booking in payments.Select(item => item.Booking).Distinct())
        {
            if (booking.Slots.Any())
                foreach (var slot in booking.Slots)
                    _scheduleRealtime.Publish(new ScheduleChangedEvent(slot.Court.VenueId, slot.CourtId,
                        slot.StartTime, slot.EndTime, booking.Status, "Updated"));
            else
                _scheduleRealtime.Publish(new ScheduleChangedEvent(booking.Court.VenueId, booking.CourtId,
                    booking.StartTime, booking.EndTime, booking.Status, "Updated"));
        }
    }

    private static SePayWebhookResult Success() => new(true, 200, null);
    private static SePayWebhookResult Fail(int statusCode, string message) => new(false, statusCode, message);
}

public sealed record SePayWebhookResult(bool Success, int StatusCode, string? Message);

public sealed class SePayWebhookRequest
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("accountNumber")] public string AccountNumber { get; set; } = string.Empty;
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("transferType")] public string TransferType { get; set; } = string.Empty;
    [JsonPropertyName("transferAmount")] public decimal TransferAmount { get; set; }
    [JsonPropertyName("referenceCode")] public string? ReferenceCode { get; set; }
}


