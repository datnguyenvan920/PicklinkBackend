using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Notifications.Implementations;

namespace PicklinkBackend.Services.ListingFees.Implementations;

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
            var paymentRepository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
            var now = DateTime.UtcNow;
            var todayStart = now.Date;
            var expiringThreshold = now.AddDays(7);

            if (!await paymentRepository.IsListingFeeSchemaReadyAsync(cancellationToken))
            {
                _logger.LogDebug("Skipping listing fee reminders because listing fee schema is not ready.");
                return;
            }

            var expiringVenues = await paymentRepository.GetExpiringListingFeeVenuesAsync(now, expiringThreshold, cancellationToken);
            if (expiringVenues.Count == 0) return;

            foreach (var venue in expiringVenues)
            {
                var linkTo = $"/owner/courts/{venue.VenueId}";
                var alreadySentToday = await paymentRepository.HasSentListingFeeReminderTodayAsync(venue.OwnerUserId, linkTo, todayStart, cancellationToken);
                if (alreadySentToday) continue;

                var notification = notifications.Add(new NotificationInput(
                    UserId: venue.OwnerUserId,
                    Type: NotificationTypes.Court,
                    Title: "Phi len san sap het han",
                    Message: $"Phi len san cua cum san \"{venue.VenueName}\" se het han vao ngay {venue.PaidUntil:dd/MM/yyyy}. Hay gui bien lai gia han de san tiep tuc hien thi tren Picklink.",
                    Tone: NotificationTones.Urgent,
                    LinkTo: linkTo,
                    LinkLabel: "Gia han phi len san"));
                await paymentRepository.SaveChangesAsync(cancellationToken);
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
}
