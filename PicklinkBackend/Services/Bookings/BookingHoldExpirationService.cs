using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Matches;
using PicklinkBackend.Services.Payments;
using PicklinkBackend.Services.Schedules;

namespace PicklinkBackend.Services.Bookings;

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
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTime.UtcNow;
            var ids = await dbContext.Bookings.AsNoTracking()
                .Where(booking =>
                    booking.Status == "Holding"
                    && booking.HoldExpiresAt <= now
                    && !booking.Payments.Any(payment => payment.Status == "WaitingForConfirmation"))
                .OrderBy(booking => booking.HoldExpiresAt)
                .Select(booking => booking.BookingId)
                .Take(100)
                .ToListAsync(cancellationToken);

            foreach (var bookingId in ids)
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                if (!await SqlServerBookingLock.AcquireAsync(dbContext, transaction, $"booking-payment:{bookingId}", cancellationToken))
                    continue;

                var booking = await dbContext.Bookings
                    .Include(item => item.Court)
                    .Include(item => item.Slots).ThenInclude(slot => slot.Court)
                    .Include(item => item.Payments)
                    .Include(item => item.Match)
                    .SingleOrDefaultAsync(item =>
                        item.BookingId == bookingId
                        && item.Status == "Holding"
                        && item.HoldExpiresAt <= now
                        && !item.Payments.Any(payment => payment.Status == "WaitingForConfirmation"),
                        cancellationToken);
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
                dbContext.BookingStatusHistories.Add(new BookingStatusHistory
                {
                    BookingId = booking.BookingId,
                    FromStatus = "Holding",
                    ToStatus = "Expired",
                    Reason = "Tự động hết hạn sau thời gian giữ chỗ",
                    ChangedAt = now
                });
                await dbContext.SaveChangesAsync(cancellationToken);
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

            await ExpireTicketBatchAsync(dbContext, now, cancellationToken);
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
        ApplicationDbContext dbContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.SessionTickets.AsNoTracking()
            .Where(ticket =>
                ticket.Status == "PendingPayment"
                && ticket.Payment.Status == "Pending"
                && (!ticket.HoldExpiresAt.HasValue || ticket.HoldExpiresAt <= now))
            .OrderBy(ticket => ticket.HoldExpiresAt)
            .ThenBy(ticket => ticket.SessionTicketId)
            .Select(ticket => new { ticket.SessionTicketId, ticket.TicketSessionId })
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            if (!await SqlServerBookingLock.AcquireAsync(
                    dbContext,
                    transaction,
                    $"ticket-session:{candidate.TicketSessionId}",
                    cancellationToken))
                continue;

            var ticket = await dbContext.SessionTickets
                .Include(item => item.Payment).ThenInclude(item => item.StatusHistories)
                .Include(item => item.TicketSession).ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.Court)
                .SingleOrDefaultAsync(item =>
                    item.SessionTicketId == candidate.SessionTicketId
                    && item.Status == "PendingPayment"
                    && item.Payment.Status == "Pending"
                    && (!item.HoldExpiresAt.HasValue || item.HoldExpiresAt <= now),
                    cancellationToken);
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
            await dbContext.SaveChangesAsync(cancellationToken);
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
