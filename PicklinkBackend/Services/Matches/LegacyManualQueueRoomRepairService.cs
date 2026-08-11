using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Matches.Implementations;

namespace PicklinkBackend.Services.Matches;

/// <summary>
/// Materializes rooms for public manual queues created before room creation became
/// part of the queue creation transaction. The repair is idempotent and serialized
/// with the normal manual-room endpoint so multiple API instances cannot create
/// duplicate rooms.
/// </summary>
public sealed class LegacyManualQueueRoomRepairService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private readonly ILogger<LegacyManualQueueRoomRepairService> _logger;

    public LegacyManualQueueRoomRepairService(
        IServiceScopeFactory scopeFactory,
        MatchRealtimeNotifier matchRealtime,
        ILogger<LegacyManualQueueRoomRepairService> logger)
    {
        _scopeFactory = scopeFactory;
        _matchRealtime = matchRealtime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RepairMissingRoomsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not repair legacy public manual queues.");
        }
    }

    private async Task RepairMissingRoomsAsync(CancellationToken cancellationToken)
    {
        List<int> queueIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IMatchRepository>();
            queueIds = await repository.MatchmakingQueues
                .AsNoTracking()
                .Where(queue => queue.IsPublic
                    && !queue.MatchId.HasValue
                    && queue.QueuePlayers.Any(player => player.IsHost))
                .OrderBy(queue => queue.MatchmakingQueueId)
                .Select(queue => queue.MatchmakingQueueId)
                .ToListAsync(cancellationToken);
        }

        foreach (var queueId in queueIds)
        {
            try
            {
                await RepairQueueAsync(queueId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Could not create the missing room for legacy manual queue {QueueId}.",
                    queueId);
            }
        }
    }

    private async Task RepairQueueAsync(int queueId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMatchRepository>();
        var synchronization = scope.ServiceProvider.GetRequiredService<MatchQueueSynchronizationService>();

        await using var transaction = await repository.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction,
                $"matchmaking-queue:{queueId}",
                cancellationToken))
        {
            _logger.LogWarning(
                "Skipped legacy manual queue {QueueId} because another process is handling it.",
                queueId);
            return;
        }

        var queue = await repository.MatchmakingQueues
            .Include(item => item.QueueSlots)
            .Include(item => item.QueuePlayers)
            .SingleOrDefaultAsync(
                item => item.MatchmakingQueueId == queueId,
                cancellationToken);
        if (queue is null || !queue.IsPublic || queue.MatchId.HasValue)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var match = await synchronization.CreateManualMatchForQueueAsync(queue, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await synchronization.SyncQueueToFirebaseAsync(queue, cancellationToken);
        _matchRealtime.Publish(match.MatchId, "MatchCreated");
        _logger.LogInformation(
            "Created room {MatchId} for legacy manual queue {QueueId}.",
            match.MatchId,
            queueId);
    }
}
