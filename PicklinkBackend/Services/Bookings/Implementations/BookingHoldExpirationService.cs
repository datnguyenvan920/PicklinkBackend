using System.Data;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Matches;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Notifications.Implementations;
using PicklinkBackend.Services.Payments;
using PicklinkBackend.Services.Schedules;

namespace PicklinkBackend.Services.Bookings.Implementations;

public class BookingHoldExpirationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingHoldExpirationService> _logger;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private readonly PaymentRealtimeNotifier _paymentRealtime;

    public BookingHoldExpirationService(
        IServiceScopeFactory scopeFactory,
        ILogger<BookingHoldExpirationService> logger,
        ScheduleRealtimeNotifier scheduleRealtime,
        MatchRealtimeNotifier matchRealtime,
        PaymentRealtimeNotifier paymentRealtime)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _scheduleRealtime = scheduleRealtime;
        _matchRealtime = matchRealtime;
        _paymentRealtime = paymentRealtime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            await ExpireBatchAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await ExpireBatchAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private async Task ExpireBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bookingRepository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
            var now = DateTime.UtcNow;
            var ids = await bookingRepository.GetStaleHoldingBookingIdsAsync(now, cancellationToken);

            foreach (var bookingId in ids)
            {
                await using var transaction = await bookingRepository.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);
                if (!await SqlServerBookingLock.AcquireAsync(
                        transaction, $"booking-payment:{bookingId}", cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                var booking = await bookingRepository.GetHoldingBookingForExpirationAsync(
                    bookingId, now, cancellationToken);
                if (booking is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                var paymentDecision = MatchPaymentDeadlinePolicy.Decide(booking);
                var needsRefund = paymentDecision == MatchPaymentDeadlineDecision.ExpireAndRefund;

                booking.Status = "Expired";
                booking.HoldExpiresAt = null;
                booking.HoldRemainingSeconds = null;
                if (booking.Match is not null)
                {
                    var memberCount = booking.Match.MatchParticipants.Count(participant =>
                        MatchRoomLifecyclePolicy.IsRoomMemberStatus(participant.Status));
                    booking.Match.Status = MatchRoomLifecyclePolicy.RoomStatusFor(
                        memberCount, booking.Match.RequiredPlayerCount);
                    booking.Match.CancelledAt = null;
                }

                var changedPayments = ExpirePayments(booking, needsRefund, now);
                if (needsRefund)
                {
                    foreach (var payment in changedPayments.Where(p => p.Status == "RefundPending"))
                    {
                        var recipientUserId = payment.Payer?.UserId ?? booking.Player?.UserId;
                        if (recipientUserId.HasValue && recipientUserId.Value > 0)
                        {
                            var venueName = booking.Court?.Venue?.VenueName ?? "cụm sân";
                            var bookingCode = booking.BookingCode ?? $"#{booking.BookingId}";
                            notifications.Add(new NotificationInput(
                                UserId: recipientUserId.Value,
                                Type: booking.MatchId.HasValue ? NotificationTypes.Match : NotificationTypes.Court,
                                Title: "Booking cần hoàn tiền",
                                Message: $"Đơn đặt sân {bookingCode} tại {venueName} đã bị hủy do quá hạn thanh toán ghép trận; số tiền {payment.Amount:N0}đ đang chờ hoàn lại.",
                                Tone: NotificationTones.Urgent,
                                LinkTo: booking.MatchId.HasValue ? $"/matches/{booking.MatchId.Value}" : "/my-bookings",
                                LinkLabel: "Xem chi tiết"));
                        }
                    }
                }

                await bookingRepository.AddBookingStatusHistoryAsync(new BookingStatusHistory
                {
                    BookingId = booking.BookingId,
                    FromStatus = "Holding",
                    ToStatus = "Expired",
                    Reason = needsRefund
                        ? "Tự động hủy vì vẫn thiếu phần thanh toán sau thời hạn 20 phút."
                        : "Tự động hết hạn sau thời gian giữ chỗ.",
                    ChangedAt = now
                }, cancellationToken);
                await bookingRepository.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                notifications.PublishPending();

                foreach (var payment in changedPayments)
                {
                    _paymentRealtime.Publish(new PaymentChangedEvent(
                        payment.PaymentId,
                        booking.BookingId,
                        booking.Court.VenueId,
                        payment.Status,
                        needsRefund ? "MatchPaymentDeadlineExpired" : "BookingExpired"));
                }
                PublishReleasedSlots(booking);
                if (booking.MatchId.HasValue)
                    _matchRealtime.Publish(booking.MatchId.Value, "BookingExpired");
            }

            await ExpireTicketBatchAsync(bookingRepository, now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to expire booking holdings.");
        }
    }

    private static List<Payment> ExpirePayments(Booking booking, bool needsRefund, DateTime now)
    {
        var changedPayments = new List<Payment>();
        foreach (var payment in booking.Payments)
        {
            var previousStatus = payment.Status;
            var nextStatus = needsRefund && previousStatus is "Paid" or "WaitingForConfirmation"
                ? "RefundPending"
                : previousStatus is "Pending" or "WaitingForConfirmation"
                    ? "Expired"
                    : null;
            if (nextStatus is null) continue;

            payment.Status = nextStatus;
            payment.AllowPaymentByOthers = false;
            ClearPaymentClaim(payment);
            payment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = previousStatus,
                ToStatus = nextStatus,
                Action = needsRefund ? "MatchPaymentDeadlineExpired" : "BookingExpired",
                Reason = nextStatus == "RefundPending"
                    ? "Booking bị hủy do thành viên còn lại chưa thanh toán; khoản đã chuyển đang chờ hoàn tiền."
                    : "Hết thời gian giữ chỗ.",
                CreatedAt = now
            });
            changedPayments.Add(payment);
        }
        return changedPayments;
    }

    private static void ClearPaymentClaim(Payment payment)
    {
        payment.ClaimedByPlayerId = null;
        payment.ClaimExpiresAt = null;
        payment.PaymentGroupId = null;
        payment.TransferContent = null;
        payment.QrImageUrl = null;
    }

    private void PublishReleasedSlots(Booking booking)
    {
        if (booking.Slots.Count > 0)
        {
            foreach (var slot in booking.Slots)
            {
                _scheduleRealtime.Publish(new ScheduleChangedEvent(
                    booking.Court.VenueId, slot.CourtId, slot.StartTime, slot.EndTime, "Expired", "Deleted"));
            }
            return;
        }

        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, "Expired", "Deleted"));
    }

    private async Task ExpireTicketBatchAsync(
        IBookingRepository bookingRepository,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var candidates = await bookingRepository.GetStaleSessionTicketCandidatesAsync(now, cancellationToken);

        foreach (var candidate in candidates)
        {
            await using var transaction = await bookingRepository.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            var ticket = await bookingRepository.GetSessionTicketForExpirationAsync(
                candidate.SessionTicketId, now, cancellationToken);
            if (ticket is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            ticket.Status = "Expired";
            ticket.HoldExpiresAt = null;
            ticket.Payment.Status = "Expired";
            ticket.Payment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = "Pending",
                ToStatus = "Expired",
                Action = "TicketHoldExpired",
                Reason = "Hết thời gian giữ chỗ mua vé.",
                CreatedAt = now
            });
            await bookingRepository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _paymentRealtime.Publish(new PaymentChangedEvent(
                ticket.PaymentId,
                ticket.TicketSession.BookingId,
                ticket.TicketSession.Booking.Court.VenueId,
                "Expired",
                "TicketHoldExpired"));
            _scheduleRealtime.Publish(new ScheduleChangedEvent(
                ticket.TicketSession.Booking.Court.VenueId,
                ticket.TicketSession.Booking.CourtId,
                ticket.TicketSession.Booking.StartTime,
                ticket.TicketSession.Booking.EndTime,
                "TicketSession",
                "Updated"));
        }
    }
}
