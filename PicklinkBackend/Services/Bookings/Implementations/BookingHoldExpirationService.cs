using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Matches;
using PicklinkBackend.Services.Matches.Implementations;
using PicklinkBackend.Services.Payments;
using PicklinkBackend.Services.Schedules;

namespace PicklinkBackend.Services.Bookings.Implementations;

public class BookingHoldExpirationService : BackgroundService
{
    private static readonly TimeSpan MatchPaymentRescueWindow = TimeSpan.FromMinutes(10);
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
            var matchRepository = scope.ServiceProvider.GetRequiredService<IMatchRepository>();
            var matchQueueSync = scope.ServiceProvider.GetRequiredService<MatchQueueSynchronizationService>();
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
                if (paymentDecision == MatchPaymentDeadlineDecision.StartRescue)
                {
                    StartMatchPaymentRescue(booking, now);
                    await bookingRepository.AddBookingStatusHistoryAsync(new BookingStatusHistory
                    {
                        BookingId = booking.BookingId,
                        FromStatus = "Holding",
                        ToStatus = "Holding",
                        Reason = "Mở thêm 10 phút để hoàn tất các phần thanh toán còn thiếu.",
                        ChangedAt = now
                    }, cancellationToken);
                    await bookingRepository.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    PublishPendingPayments(booking, MatchPaymentDeadlinePolicy.RescueAction);
                    _matchRealtime.Publish(booking.MatchId!.Value, MatchPaymentDeadlinePolicy.RescueAction);
                    continue;
                }

                var needsRefund = paymentDecision == MatchPaymentDeadlineDecision.ExpireAndRefund;
                MatchmakingQueue? linkedQueue = null;
                if (needsRefund)
                {
                    linkedQueue = await RemoveUnpaidMatchParticipantsAsync(
                        booking, matchRepository, matchQueueSync, now, cancellationToken);
                }

                booking.Status = "Expired";
                booking.HoldExpiresAt = null;
                booking.HoldRemainingSeconds = null;
                if (booking.Match is not null)
                {
                    booking.Match.Status = needsRefund ? "Recruiting" : "ReadyToBook";
                    booking.Match.CancelledAt = null;
                }

                var changedPayments = ExpirePayments(booking, needsRefund, now);
                await bookingRepository.AddBookingStatusHistoryAsync(new BookingStatusHistory
                {
                    BookingId = booking.BookingId,
                    FromStatus = "Holding",
                    ToStatus = "Expired",
                    Reason = needsRefund
                        ? "Tự động hủy vì vẫn thiếu phần thanh toán sau 10 phút gia hạn."
                        : "Tự động hết hạn sau thời gian giữ chỗ.",
                    ChangedAt = now
                }, cancellationToken);
                await bookingRepository.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                foreach (var payment in changedPayments)
                {
                    _paymentRealtime.Publish(new PaymentChangedEvent(
                        payment.PaymentId,
                        booking.BookingId,
                        booking.Court.VenueId,
                        payment.Status,
                        needsRefund ? "MatchPaymentRescueExpired" : "BookingExpired"));
                }
                PublishReleasedSlots(booking);
                if (booking.MatchId.HasValue)
                    _matchRealtime.Publish(booking.MatchId.Value, "BookingExpired");
                if (needsRefund)
                    await matchQueueSync.SyncQueueToFirebaseAsync(linkedQueue, cancellationToken);
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

    private static void StartMatchPaymentRescue(Booking booking, DateTime now)
    {
        booking.HoldExpiresAt = now.Add(MatchPaymentRescueWindow);
        booking.HoldRemainingSeconds = null;
        booking.Match!.Status = "BookingPending";

        foreach (var payment in booking.Payments.Where(payment => payment.Status == "Pending"))
        {
            payment.AllowPaymentByOthers = true;
            ClearPaymentClaim(payment);
            payment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = "Pending",
                ToStatus = "Pending",
                Action = MatchPaymentDeadlinePolicy.RescueAction,
                Reason = "Mở thêm 10 phút; mọi thành viên đều có thể thanh toán phần còn thiếu.",
                CreatedAt = now
            });
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
                Action = needsRefund ? "MatchPaymentRescueExpired" : "BookingExpired",
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

    private void PublishPendingPayments(Booking booking, string action)
    {
        foreach (var payment in booking.Payments.Where(payment => payment.Status == "Pending"))
        {
            _paymentRealtime.Publish(new PaymentChangedEvent(
                payment.PaymentId, booking.BookingId, booking.Court.VenueId, "Pending", action));
        }
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

    private static async Task<MatchmakingQueue?> RemoveUnpaidMatchParticipantsAsync(
        Booking booking,
        IMatchRepository matchRepository,
        MatchQueueSynchronizationService matchQueueSync,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var match = booking.Match!;
        var unpaidPlayerIds = booking.Payments
            .Where(payment => payment.Status == "Pending")
            .Select(payment => payment.PayerId)
            .ToHashSet();
        var unpaidParticipants = match.MatchParticipants
            .Where(participant => unpaidPlayerIds.Contains(participant.PlayerId) && IsApproved(participant.Status))
            .ToList();
        if (unpaidParticipants.Count == 0) return null;

        var remainingParticipants = match.MatchParticipants
            .Where(participant => !unpaidPlayerIds.Contains(participant.PlayerId) && IsApproved(participant.Status))
            .OrderBy(participant => participant.RequestedAt)
            .ToList();
        MatchParticipant? newHost = null;
        if (match.HostPlayerId.HasValue && unpaidPlayerIds.Contains(match.HostPlayerId.Value))
        {
            newHost = remainingParticipants.FirstOrDefault();
            match.HostPlayerId = newHost?.PlayerId;
            if (newHost is not null) newHost.IsHost = true;
        }

        foreach (var participant in unpaidParticipants)
        {
            participant.Status = "Removed";
            participant.IsHost = false;
            participant.RespondedAt = now;
        }

        var lobbyConversationIds = await matchRepository.Conversations
            .Where(conversation => conversation.MatchId == match.MatchId
                && conversation.ConversationType == "LobbyChat")
            .Select(conversation => conversation.ConversationId)
            .ToListAsync(cancellationToken);
        var unpaidUserIds = unpaidParticipants.Select(participant => participant.Player.UserId).ToList();
        var memberships = await matchRepository.ConversationParticipants
            .Where(participant => lobbyConversationIds.Contains(participant.ConversationId)
                && unpaidUserIds.Contains(participant.UserId))
            .ToListAsync(cancellationToken);
        await matchRepository.RemoveRangeConversationParticipantsAsync(memberships, cancellationToken);

        MatchmakingQueue? linkedQueue = null;
        foreach (var participant in unpaidParticipants)
        {
            linkedQueue = await matchQueueSync.SyncMatchParticipantToQueueAsync(
                match.MatchId, participant, cancellationToken) ?? linkedQueue;
        }
        if (newHost is not null)
        {
            linkedQueue = await matchQueueSync.SyncMatchParticipantToQueueAsync(
                match.MatchId, newHost, cancellationToken) ?? linkedQueue;
        }
        return linkedQueue;
    }

    private static bool IsApproved(string? status) =>
        string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase);

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
