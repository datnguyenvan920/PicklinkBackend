using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;

namespace PicklinkBackend.Services;

public sealed class MatchExpirationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MatchRealtimeNotifier _matchRealtime;
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
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Clamp(_configuration.GetValue("Match:ExpirationScanSeconds", 30), 5, 300);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ExpireMatchesAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(exception, "Could not expire overdue matchmaking invitations.");
                }

                await timer.WaitForNextTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ExpireMatchesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var expired = await db.Matches
            .Where(match =>
                match.AvailableDateTo.HasValue
                && match.AvailableDateTo.Value < today
                && (match.Status == "Recruiting" || match.Status == "ReadyToBook"))
            .OrderBy(match => match.AvailableDateTo)
            .Take(200)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0) return;
        foreach (var match in expired) match.Status = "Expired";
        await db.SaveChangesAsync(cancellationToken);
        foreach (var match in expired) _matchRealtime.Publish(match.MatchId, "Expired");
    }
}
