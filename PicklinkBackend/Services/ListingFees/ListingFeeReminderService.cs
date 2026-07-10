using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.Services.Notifications;

namespace PicklinkBackend.Services.ListingFees;

public sealed class ListingFeeReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ListingFeeReminderService> _logger;

    public ListingFeeReminderService(
        IServiceScopeFactory scopeFactory,
        ILogger<ListingFeeReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        try
        {
            await SendReminderBatchAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SendReminderBatchAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private async Task SendReminderBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
            var now = DateTime.UtcNow;
            var todayStart = now.Date;
            var expiringThreshold = now.AddDays(7);

            if (!await IsListingFeeSchemaReadyAsync(dbContext, cancellationToken))
            {
                _logger.LogDebug("Skipping listing fee reminders because listing fee schema is not ready.");
                return;
            }

            var latestPaidUntilByVenue = await dbContext.VenueListingPayments.AsNoTracking()
                .Where(payment => payment.Status == "Confirmed" && payment.PaidUntil != null)
                .GroupBy(payment => payment.VenueId)
                .Select(group => new
                {
                    VenueId = group.Key,
                    PaidUntil = group.Max(payment => payment.PaidUntil)
                })
                .ToListAsync(cancellationToken);

            var expiringVenueIds = latestPaidUntilByVenue
                .Where(item => item.PaidUntil >= now && item.PaidUntil <= expiringThreshold)
                .Select(item => item.VenueId)
                .ToList();
            if (expiringVenueIds.Count == 0) return;

            var paidUntilByVenue = latestPaidUntilByVenue.ToDictionary(item => item.VenueId, item => item.PaidUntil);
            var venues = await dbContext.Venues.AsNoTracking()
                .Where(venue => expiringVenueIds.Contains(venue.VenueId))
                .Select(venue => new
                {
                    venue.VenueId,
                    venue.VenueName,
                    OwnerUserId = venue.Owner.UserId
                })
                .ToListAsync(cancellationToken);

            foreach (var venue in venues)
            {
                var paidUntil = paidUntilByVenue[venue.VenueId];
                var linkTo = $"/owner/courts/{venue.VenueId}";
                var alreadySentToday = await dbContext.NotificationLogs.AsNoTracking()
                    .AnyAsync(notification =>
                        notification.UserId == venue.OwnerUserId
                        && notification.Title == "Phi len san sap het han"
                        && notification.LinkTo == linkTo
                        && notification.CreatedAt >= todayStart,
                        cancellationToken);
                if (alreadySentToday) continue;

                var notification = notifications.Add(new NotificationInput(
                    UserId: venue.OwnerUserId,
                    Type: NotificationTypes.Court,
                    Title: "Phi len san sap het han",
                    Message: $"Phi len san cua cum san \"{venue.VenueName}\" se het han vao ngay {paidUntil:dd/MM/yyyy}. Hay gui bien lai gia han de san tiep tuc hien thi tren Picklink.",
                    Tone: NotificationTones.Urgent,
                    LinkTo: linkTo,
                    LinkLabel: "Gia han phi len san"));
                await dbContext.SaveChangesAsync(cancellationToken);
                notifications.PublishCreated(notification);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to send listing fee expiry reminders.");
        }
    }

    private static async Task<bool> IsListingFeeSchemaReadyAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var result = await dbContext.Database.SqlQueryRaw<int>(
                """
                SELECT CASE
                    WHEN OBJECT_ID(N'[VENUE_LISTING_PAYMENT]', N'U') IS NULL THEN 0
                    ELSE 1
                END AS [Value]
                """)
            .SingleAsync(cancellationToken);

        return result == 1;
    }
}
