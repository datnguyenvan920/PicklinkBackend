using System.Data;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Matches;
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
            {
                await ExpireBatchAsync(stoppingToken);
            }
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
            var now = DateTime.UtcNow;
            var ids = await bookingRepository.GetStaleHoldingBookingIdsAsync(now, cancellationToken);

            foreach (var bookingId in ids)
            {
                await using var transaction = await bookingRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

                var booking = await bookingRepository.GetHoldingBookingForExpirationAsync(bookingId, now, cancellationToken);
                if (booking is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                booking.Status = "Expired";
                booking.HoldExpiresAt = null;
                if (booking.Match is not null)
                {
                    booking.Match.Status = "ReadyToBook";
                    booking.Match.CancelledAt = null;
                }
                foreach (var payment in booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation"))
                {
                    var previousPaymentStatus = payment.Status;
                    payment.Status = "Expired";
                    payment.StatusHistories.Add(new PaymentStatusHistory
                    {
                        FromStatus = previousPaymentStatus,
                        ToStatus = "Expired",
                        Action = "BookingExpired",
                        Reason = "Hết thời gian giữ chỗ",
                        CreatedAt = now
                    });
                }
                await bookingRepository.AddBookingStatusHistoryAsync(new BookingStatusHistory
                {
                    BookingId = booking.BookingId,
                    FromStatus = "Holding",
                    ToStatus = "Expired",
                    Reason = "Tự động hết hạn sau thời gian giữ chỗ",
                    ChangedAt = now
                }, cancellationToken);
                await bookingRepository.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                if (booking.Slots.Count > 0)
                {
                    foreach (var slot in booking.Slots)
                        _scheduleRealtime.Publish(new ScheduleChangedEvent(
                            booking.Court.VenueId, slot.CourtId, slot.StartTime, slot.EndTime, "Expired", "Deleted"));
                }
                else _scheduleRealtime.Publish(new ScheduleChangedEvent(
                    booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, "Expired", "Deleted"));
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

            var ticket = await bookingRepository.GetSessionTicketForExpirationAsync(candidate.SessionTicketId, now, cancellationToken);
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
                Reason = "Hết thời gian giữ chỗ mua vé",
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
