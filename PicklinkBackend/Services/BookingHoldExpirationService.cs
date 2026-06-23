using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public class BookingHoldExpirationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingHoldExpirationService> _logger;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;

    public BookingHoldExpirationService(IServiceScopeFactory scopeFactory, ILogger<BookingHoldExpirationService> logger, ScheduleRealtimeNotifier scheduleRealtime)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _scheduleRealtime = scheduleRealtime;
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
                .Where(booking => booking.Status == "Holding" && booking.HoldExpiresAt <= now)
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
                    .Include(item => item.Payments)
                    .Include(item => item.Match)
                    .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.Status == "Holding" && item.HoldExpiresAt <= now, cancellationToken);
                if (booking is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                booking.Status = "Expired";
                booking.HoldExpiresAt = null;
                if (booking.Match is not null)
                {
                    booking.Match.Status = "Cancelled";
                    booking.Match.CancelledAt = now;
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
                _scheduleRealtime.Publish(new ScheduleChangedEvent(
                    booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, "Expired", "Deleted"));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to expire booking holdings.");
        }
    }
}
