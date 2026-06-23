using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;

namespace PicklinkBackend.Services;

public sealed class MatchExpirationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MatchExpirationService> _logger;

    public MatchExpirationService(
        IServiceScopeFactory scopeFactory,
        MatchRealtimeNotifier matchRealtime,
        ScheduleRealtimeNotifier scheduleRealtime,
        IConfiguration configuration,
        ILogger<MatchExpirationService> logger)
    {
        _scopeFactory = scopeFactory;
        _matchRealtime = matchRealtime;
        _scheduleRealtime = scheduleRealtime;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireMatchesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not expire overdue matchmaking rooms.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task ExpireMatchesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var candidates = await db.Matches
            .Include(match => match.MatchParticipants)
            .Include(match => match.Bookings).ThenInclude(booking => booking.Payments)
            .Include(match => match.Bookings).ThenInclude(booking => booking.Court)
            .Where(match => match.Status == "Waiting" || match.Status == "Full" || match.Status == "PaymentPending")
            .ToListAsync(cancellationToken);

        var utcNow = DateTime.UtcNow;
        var localNow = DateTime.Now;
        var matchingMinutes = Math.Clamp(_configuration.GetValue("Match:MatchingMinutes", 15), 1, 120);
        var expired = new List<(int MatchId, int VenueId, int CourtId, DateTime Start, DateTime End)>();

        foreach (var match in candidates)
        {
            var booking = match.Bookings.OrderBy(item => item.CreatedAt).FirstOrDefault();
            if (booking is null) continue;

            var paymentExpired = match.Status == "PaymentPending"
                && booking.HoldExpiresAt.HasValue
                && booking.HoldExpiresAt.Value <= utcNow;
            var matchingDeadline = booking.HoldExpiresAt ?? match.CreatedAt.AddMinutes(matchingMinutes);
            var matchingExpired = match.Status is "Waiting" or "Full"
                && matchingDeadline <= utcNow;
            var matchStartedWithoutConfirmation = match.Status is "Waiting" or "Full"
                && booking.StartTime <= localNow;
            if (!paymentExpired && !matchingExpired && !matchStartedWithoutConfirmation) continue;

            match.Status = "Cancelled";
            match.CancelledAt = utcNow;
            booking.Status = "Expired";
            booking.HoldExpiresAt = null;

            foreach (var payment in booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation"))
                payment.Status = "Expired";

            foreach (var participant in match.MatchParticipants.Where(item => !item.IsHost && (item.Status is "Pending" or "Accepted")))
            {
                participant.Status = "Removed";
                participant.RespondedAt = utcNow;
            }

            expired.Add((match.MatchId, booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime));
        }

        if (expired.Count == 0) return;
        await db.SaveChangesAsync(cancellationToken);

        foreach (var item in expired)
        {
            _matchRealtime.Publish(item.MatchId, "Expired");
            _scheduleRealtime.Publish(new ScheduleChangedEvent(
                item.VenueId, item.CourtId, item.Start, item.End, "Expired", "Deleted"));
        }
    }
}
