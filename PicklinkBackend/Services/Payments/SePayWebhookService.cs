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
        if (await _db.SePayTransactions.AsNoTracking().AnyAsync(
                item => item.ExternalTransactionId == request.Id, cancellationToken))
            return Success();

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

        var candidatePaymentIds = await _db.Payments.AsNoTracking()
            .Where(item => candidate.PaymentGroupId.HasValue
                ? item.PaymentGroupId == candidate.PaymentGroupId
                : item.PaymentId == candidate.PaymentId)
            .Select(item => item.PaymentId)
            .ToArrayAsync(cancellationToken);
        var candidateTicketSessionIds = await _db.SessionTickets.AsNoTracking()
            .Where(item => candidatePaymentIds.Contains(item.PaymentId))
            .Select(item => item.TicketSessionId)
            .Distinct()
            .OrderBy(item => item)
            .ToArrayAsync(cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"sepay-transaction:{request.Id}", cancellationToken))
            return Fail(409, "Transaction is being processed.");
        if (await _db.SePayTransactions.AnyAsync(
                item => item.ExternalTransactionId == request.Id, cancellationToken))
            return Success();
        foreach (var ticketSessionId in candidateTicketSessionIds)
        {
            if (!await SqlServerBookingLock.AcquireAsync(
                    _db, transaction, $"ticket-session:{ticketSessionId}", cancellationToken))
                return Fail(409, "Ticket session is being updated.");
        }
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

        var paymentIds = groupPayments.Select(item => item.PaymentId).ToArray();
        var sessionTickets = await _db.SessionTickets
            .Include(item => item.TicketSession).ThenInclude(item => item.Booking)
            .Where(item => paymentIds.Contains(item.PaymentId))
            .ToListAsync(cancellationToken);
        var ticketsByPaymentId = sessionTickets.ToDictionary(item => item.PaymentId);
        var paymentsById = groupPayments.ToDictionary(item => item.PaymentId);

        var matchIds = bookings.Where(item => item.MatchId.HasValue)
            .Select(item => item.MatchId!.Value).Distinct().ToArray();
        if (matchIds.Length > 0)
            await _db.Matches.Include(item => item.MatchParticipants)
                .Where(item => matchIds.Contains(item.MatchId)).LoadAsync(cancellationToken);
        await _db.BookingSlots.Include(item => item.Court)
            .Where(item => bookingIds.Contains(item.BookingId)).LoadAsync(cancellationToken);

        var payment = groupPayments[0];
        var utcNow = DateTime.UtcNow;
        var rawReference = string.IsNullOrWhiteSpace(request.ReferenceCode)
            ? request.Id.ToString()
            : request.ReferenceCode.Trim();
        var reference = Truncate(rawReference, 120);
        var allTicketPayments = sessionTickets.Count > 0
            && sessionTickets.Count == groupPayments.Count;
        var isAdditionalTicketTransfer = allTicketPayments
            && sessionTickets.All(item =>
                item.Status is "Paid" or "CheckedIn" or "RefundPending" or "Refunded")
            && groupPayments.All(item =>
                item.Status is "Paid" or "RefundPending" or "Refunded");
        if (isAdditionalTicketTransfer)
        {
            _db.SePayTransactions.Add(NewSePayTransaction(
                request, payment.PaymentId, "AdditionalRefundPending", utcNow));
            NotifyAdditionalTicketTransfer(
                payment,
                sessionTickets[0],
                request.TransferAmount,
                reference,
                utcNow,
                "Khoản chuyển thêm sau khi giao dịch vé đã được xử lý.");
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _notifications.PublishPending();
            _paymentRealtime.Publish(new PaymentChangedEvent(
                payment.PaymentId,
                payment.BookingId,
                payment.Booking.Court.VenueId,
                payment.Status,
                "AdditionalRefundPending"));
            return Success();
        }
        if (groupPayments.All(item => item.Status == "Paid"))
        {
            _db.SePayTransactions.Add(NewSePayTransaction(
                request, payment.PaymentId, "ReviewRequired", utcNow));
            NotifyPaymentReviewRequired(
                payment,
                request.TransferAmount,
                reference,
                "Giao dịch mới dùng lại mã của payment đã thanh toán.");
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _notifications.PublishPending();
            return Success();
        }
        var lateSettlementTickets = sessionTickets.Where(item =>
            (item.Status == "Cancelled" && paymentsById[item.PaymentId].Status == "Cancelled")
            || (item.Status == "Expired" && paymentsById[item.PaymentId].Status == "Expired")
            || (item.Status == "PendingPayment"
                && (paymentsById[item.PaymentId].Status is "Pending" or "WaitingForConfirmation")
                && (!item.HoldExpiresAt.HasValue
                    || item.HoldExpiresAt <= utcNow
                    || item.TicketSession.Status != "Published"
                    || item.TicketSession.Booking.Status != "Confirmed")))
            .ToList();
        var isLateTicketSettlement = sessionTickets.Count == groupPayments.Count
            && lateSettlementTickets.Count == groupPayments.Count;
        if (!isLateTicketSettlement
            && groupPayments.Any(item => item.Status is not "Pending" and not "WaitingForConfirmation"))
            return Fail(409, "Payment is not eligible for automatic confirmation.");

        var expectedAmount = decimal.Round(groupPayments.Sum(item => (decimal)item.Amount), 0, MidpointRounding.AwayFromZero);
        if (expectedAmount != request.TransferAmount)
        {
            var ledgerStatus = allTicketPayments ? "AdditionalRefundPending" : "ReviewRequired";
            _db.SePayTransactions.Add(NewSePayTransaction(
                request, payment.PaymentId, ledgerStatus, utcNow));
            if (allTicketPayments)
                NotifyAdditionalTicketTransfer(
                    payment,
                    sessionTickets[0],
                    request.TransferAmount,
                    reference,
                    utcNow,
                    $"Số tiền không khớp; hệ thống chờ {expectedAmount:0} VND.");
            else
                NotifyPaymentReviewRequired(
                    payment,
                    request.TransferAmount,
                    reference,
                    $"Số tiền không khớp; hệ thống chờ {expectedAmount:0} VND.");
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _notifications.PublishPending();
            return Success();
        }
        if (isLateTicketSettlement)
        {
            _db.SePayTransactions.Add(NewSePayTransaction(
                request, payment.PaymentId, "TicketRefundPending", utcNow));
            foreach (var ticket in lateSettlementTickets)
                MarkLateTicketSettlement(paymentsById[ticket.PaymentId], ticket, utcNow, reference);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _notifications.PublishPending();
            PublishUpdates(groupPayments);
            return Success();
        }
        if (groupPayments.Any(item => !ticketsByPaymentId.ContainsKey(item.PaymentId)
                && (item.Booking.Status != "Holding" || item.Booking.HoldExpiresAt <= utcNow)))
            return Fail(409, "Booking hold has expired.");
        if (sessionTickets.Any(item => item.Status != "PendingPayment"
                || item.HoldExpiresAt <= utcNow
                || item.TicketSession.Status != "Published"
                || item.TicketSession.Booking.Status != "Confirmed"))
            return Fail(409, "Ticket payment hold has expired or the session is closed.");
        if (payment.Booking.MatchId is int matchId
            && !await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Fail(409, "Match is being updated.");

        var now = utcNow;
        foreach (var item in groupPayments)
        {
            ticketsByPaymentId.TryGetValue(item.PaymentId, out var ticket);
            ConfirmPayment(item, ticket, now, reference);
        }
        foreach (var booking in groupPayments
                     .Where(item => !ticketsByPaymentId.ContainsKey(item.PaymentId))
                     .Select(item => item.Booking)
                     .Distinct())
            FinalizeBooking(booking, now, reference);

        _db.SePayTransactions.Add(NewSePayTransaction(
            request, payment.PaymentId, "Applied", utcNow));
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishUpdates(groupPayments);
        return Success();
    }

    private static SePayTransaction NewSePayTransaction(
        SePayWebhookRequest request,
        int paymentId,
        string status,
        DateTime receivedAt) => new()
    {
        ExternalTransactionId = request.Id,
        PaymentId = paymentId,
        Amount = request.TransferAmount,
        AccountNumber = Truncate(request.AccountNumber.Trim(), 100),
        ReferenceCode = string.IsNullOrWhiteSpace(request.ReferenceCode)
            ? null
            : Truncate(request.ReferenceCode.Trim(), 200),
        Status = status,
        ReceivedAt = receivedAt
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private void NotifyAdditionalTicketTransfer(
        Payment payment,
        SessionTicket ticket,
        decimal amount,
        string reference,
        DateTime now,
        string reason)
    {
        _db.VenueAuditLogs.Add(new VenueAuditLog
        {
            VenueId = payment.Booking.Court.VenueId,
            ActorId = payment.Booking.Court.Venue.Owner.UserId,
            Action = $"TicketAdditionalTransfer:{ticket.SessionTicketId}:SePay:{reference}",
            Timestamp = now
        });
        _notifications.Add(new NotificationInput(
            payment.Payer.UserId,
            NotificationTypes.Ticket,
            "Khoản chuyển vé đang chờ hoàn",
            $"Khoản chuyển {amount:0} VND cho vé {ticket.TicketCode} không được dùng để kích hoạt vé và đang chờ Owner hoàn lại.",
            NotificationTones.Urgent,
            $"/my-tickets/{ticket.SessionTicketId}",
            "Xem giao dịch"));
        _notifications.Add(new NotificationInput(
            payment.Booking.Court.Venue.Owner.UserId,
            NotificationTypes.Ticket,
            "Có khoản chuyển vé cần hoàn thêm",
            $"{reason} Số tiền {amount:0} VND, mã đối soát {reference}.",
            NotificationTones.Urgent,
            $"/owner/ticket-sessions/{ticket.TicketSessionId}",
            "Xử lý hoàn tiền"));
    }

    private void NotifyPaymentReviewRequired(
        Payment payment,
        decimal amount,
        string reference,
        string reason)
    {
        _db.VenueAuditLogs.Add(new VenueAuditLog
        {
            VenueId = payment.Booking.Court.VenueId,
            ActorId = payment.Booking.Court.Venue.Owner.UserId,
            Action = $"PaymentTransferReviewRequired:{payment.PaymentId}:SePay:{reference}",
            Timestamp = DateTime.UtcNow
        });
        _notifications.Add(new NotificationInput(
            payment.Booking.Court.Venue.Owner.UserId,
            NotificationTypes.Payment,
            "Có giao dịch cần đối soát",
            $"{reason} Số tiền {amount:0} VND, mã {reference}.",
            NotificationTones.Urgent,
            $"/owner/payments/{payment.PaymentId}",
            "Xem thanh toán"));
    }

    private void MarkLateTicketSettlement(
        Payment payment,
        SessionTicket ticket,
        DateTime now,
        string reference)
    {
        var previous = payment.Status;
        payment.Status = "RefundPending";
        payment.PaidAt ??= now;
        payment.VerifiedAt = now;
        payment.VerifiedByUserId = null;
        payment.RejectionReason = null;
        ticket.Status = "RefundPending";
        ticket.HoldExpiresAt = null;
        payment.StatusHistories.Add(new PaymentStatusHistory
        {
            FromStatus = previous,
            ToStatus = "RefundPending",
            Action = "LateSePaySettlement",
            Reason = $"SePay transaction {reference} arrived after the ticket hold closed.",
            CreatedAt = now
        });
        _db.VenueAuditLogs.Add(new VenueAuditLog
        {
            VenueId = payment.Booking.Court.VenueId,
            ActorId = payment.Booking.Court.Venue.Owner.UserId,
            Action = $"TicketLatePaymentRefundPending:{ticket.SessionTicketId}:SePay:{reference}",
            Timestamp = now
        });
        _notifications.Add(new NotificationInput(
            payment.Payer.UserId,
            NotificationTypes.Ticket,
            "Khoản chuyển vé đang chờ hoàn tiền",
            $"Hệ thống nhận thanh toán cho vé {ticket.TicketCode} sau khi thời gian giữ chỗ đã đóng. Khoản tiền đang chờ Owner hoàn lại.",
            NotificationTones.Urgent,
            $"/my-tickets/{ticket.SessionTicketId}",
            "Xem vé"));
        _notifications.Add(new NotificationInput(
            payment.Booking.Court.Venue.Owner.UserId,
            NotificationTypes.Ticket,
            "Có khoản vé cần hoàn tiền",
            $"Khoản chuyển đến muộn cho vé {ticket.TicketCode} đã được ghi nhận và cần hoàn lại cho Player.",
            NotificationTones.Urgent,
            $"/owner/ticket-sessions/{ticket.TicketSessionId}",
            "Xử lý hoàn tiền"));
    }

    private void ConfirmPayment(Payment payment, SessionTicket? ticket, DateTime now, string reference)
    {
        var previous = payment.Status;
        payment.Status = "Paid";
        payment.PaidAt = now;
        payment.VerifiedAt = now;
        payment.VerifiedByUserId = null;
        payment.RejectionReason = null;
        if (ticket is not null)
        {
            ticket.Status = "Paid";
            ticket.HoldExpiresAt = null;
        }
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
        _notifications.Add(ticket is null
            ? new NotificationInput(
                payment.Payer.UserId, NotificationTypes.Payment, "Thanh toán đã được xác nhận",
                $"Thanh toán cho booking {payment.Booking.BookingCode ?? $"PL-{payment.BookingId}"} đã được SePay xác nhận.",
                NotificationTones.Success, "/my-bookings", "Xem đặt sân")
            : new NotificationInput(
                payment.Payer.UserId, NotificationTypes.Ticket, "Vé đã thanh toán",
                $"Vé {ticket.TicketCode} đã được SePay xác nhận thanh toán.",
                NotificationTones.Success, $"/my-tickets/{ticket.SessionTicketId}", "Xem vé"));
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


