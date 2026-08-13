using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PicklinkBackend.Data;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Matches;

public class MatchmakingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IFirebaseService? _firebaseService;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private readonly NotificationRealtimeNotifier _notificationRealtime;
    private readonly ILogger<MatchmakingWorker> _logger;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private DateTime _lastCleanupDate = DateTime.MinValue;

    public MatchmakingWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<MatchmakingWorker> logger,
        MatchRealtimeNotifier matchRealtime,
        NotificationRealtimeNotifier notificationRealtime,
        IFirebaseService? firebaseService = null)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
        _matchRealtime = matchRealtime;
        _notificationRealtime = notificationRealtime;
        _firebaseService = firebaseService;
    }

    private static bool IsApproved(MatchmakingQueuePlayer queuePlayer) => queuePlayer.Status == "Approved";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var scanIntervalSeconds = Math.Clamp(_configuration.GetValue("MatchmakingWorker:ScanIntervalSeconds", 30), 5, 120);
        _logger.LogInformation("MatchmakingWorker started in API host. Reactive scan with fallback interval: {seconds} seconds.", scanIntervalSeconds);

        IDisposable? firebaseSubscription = null;
        if (_firebaseService != null && _firebaseService.IsConfigured)
        {
            try
            {
                var observable = _firebaseService.SubscribeToQueueChanges<object>();
                if (observable != null)
                {
                    _logger.LogInformation("MatchmakingWorker: Successfully subscribed to Firebase Realtime Database event stream.");
                    firebaseSubscription = observable.Subscribe(async e =>
                    {
                        try
                        {
                            _logger.LogInformation("MatchmakingWorker: Firebase event received ({event}). Running instant match scan...", e.EventType);
                            await RunMatchmakingScanAsync(stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error occurred during reactive Firebase matchmaking scan.");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to subscribe to Firebase Realtime Database events. Falling back to periodic timer.");
            }
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(scanIntervalSeconds));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                try
                {
                    await RunMatchmakingScanAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during periodic matchmaking scan. The worker will retry on the next interval.");
                }
            }
        }
        finally
        {
            firebaseSubscription?.Dispose();
        }
    }

    private async Task RunMatchmakingScanAsync(CancellationToken cancellationToken)
    {
        if (!await _scanGate.WaitAsync(0, cancellationToken)) return;

        try
        {
            await RunMatchmakingScanCoreAsync(cancellationToken);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task RunMatchmakingScanCoreAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // 1. Clean dead/stale queue records & overdue items
        try
        {
            var matchmakingService = scope.ServiceProvider.GetRequiredService<Implementations.MatchmakingService>();
            var cleanupResult = await matchmakingService.ClearOverdue(cancellationToken);
            if (cleanupResult.Value is { } result && (result.ExpiredQueuesCount > 0 || result.ExpiredMatchesCount > 0 || result.CompletedMatchesCount > 0 || result.DeletedDeadQueuesCount > 0))
            {
                _logger.LogInformation("MatchmakingWorker Overdue Cleanup: Expired {expiredQueues} queues, {expiredMatches} matches, completed {completedMatches} matches, deleted {deletedDead} dead queues.",
                    result.ExpiredQueuesCount, result.ExpiredMatchesCount, result.CompletedMatchesCount, result.DeletedDeadQueuesCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during MatchmakingWorker overdue cleanup.");
        }

        // 2. Fetch active queue entries
        var queueItems = await db.MatchmakingQueues
            .Where(q => q.IsActive && (!q.IsPublic || q.MatchId.HasValue))
            .Include(q => q.QueueSlots)
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .OrderBy(q => q.UpdatedAt)
            .ToListAsync(cancellationToken);

        if (queueItems.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Scanning matchmaking queue. Active queue size: {count}.", queueItems.Count);

        var grouped = queueItems.GroupBy(q => q.MatchType);
        var matchedPlayerIds = new HashSet<int>();

        foreach (var group in grouped)
        {
            var candidates = group.ToList();
            var matchedQueueIds = new HashSet<int>();
            var applyFailed = false;

            for (int geoLevel = 3; geoLevel >= 1; geoLevel--)
            {
                while (TryFindCompatibleGroup(
                    candidates.Where(q => !matchedQueueIds.Contains(q.MatchmakingQueueId)
                        && q.QueuePlayers.Where(IsApproved).All(player => !matchedPlayerIds.Contains(player.PlayerId)))
                        .ToList(),
                    geoLevel,
                    VietnamTime.Now,
                    out var matchedQueues,
                    out var matchedDate,
                    out var matchedTimeStart,
                    out var matchedTimeEnd))
                {
                    _logger.LogInformation(
                        "Match found at GeoLevel {level} between queues {queueIds} on {date} at {start}-{end}.",
                        geoLevel,
                        string.Join(", ", matchedQueues.Select(q => q.MatchmakingQueueId)),
                        matchedDate,
                        matchedTimeStart,
                        matchedTimeEnd);

                    var success = await ApplyMatchAsync(
                        db,
                        matchedQueues,
                        matchedDate,
                        matchedTimeStart,
                        matchedTimeEnd,
                        cancellationToken);

                    if (!success)
                    {
                        applyFailed = true;
                        break;
                    }

                    foreach (var queue in matchedQueues)
                        matchedQueueIds.Add(queue.MatchmakingQueueId);
                    foreach (var playerId in matchedQueues.SelectMany(queue => queue.QueuePlayers.Where(IsApproved)).Select(player => player.PlayerId))
                        matchedPlayerIds.Add(playerId);
                }

                if (applyFailed)
                    break;
            }
        }
    }

    public static bool TryFindCompatibleGroup(
        IReadOnlyList<MatchmakingQueue> candidates,
        int geoLevel,
        DateTime now,
        out List<MatchmakingQueue> matchedQueues,
        out DateOnly matchedDate,
        out TimeOnly matchedTimeStart,
        out TimeOnly matchedTimeEnd)
    {
        List<MatchmakingQueue>? result = null;
        var resultDate = default(DateOnly);
        var resultStart = default(TimeOnly);
        var resultEnd = default(TimeOnly);
        var selected = new List<MatchmakingQueue>(8);

        bool Search(int nextIndex, int playerCount, string matchType, int capacity)
        {
            if (playerCount == capacity)
            {
                if (IsCompatibleGroup(selected, geoLevel, now, out resultDate, out resultStart, out resultEnd))
                {
                    result = selected.ToList();
                    return true;
                }

                return false;
            }

            for (var index = nextIndex; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var candidatePlayerCount = candidate.QueuePlayers.Count(IsApproved);
                if (candidate.MatchType != matchType || candidate.PlayerCount != capacity || candidatePlayerCount == 0 || playerCount + candidatePlayerCount > capacity)
                    continue;

                if (candidate.QueuePlayers.Where(IsApproved).Any(qp => selected.Any(q => q.QueuePlayers.Where(IsApproved).Any(existing => existing.PlayerId == qp.PlayerId))))
                    continue;

                if (candidate.MatchId.HasValue && selected.Any(queue =>
                        queue.MatchId.HasValue && queue.MatchId.Value != candidate.MatchId.Value))
                    continue;

                selected.Add(candidate);
                if (Search(index + 1, playerCount + candidatePlayerCount, matchType, capacity))
                    return true;
                selected.RemoveAt(selected.Count - 1);
            }

            return false;
        }

        for (var index = 0; index < candidates.Count && result is null; index++)
        {
            var candidate = candidates[index];
            var capacity = candidate.PlayerCount;
            if (capacity is < 2 or > 8 || candidate.QueuePlayers.Count(IsApproved) == 0 || candidate.QueuePlayers.Count(IsApproved) > capacity)
                continue;

            selected.Clear();
            selected.Add(candidate);
            Search(index + 1, candidate.QueuePlayers.Count(IsApproved), candidate.MatchType, capacity);
        }

        matchedQueues = result ?? new List<MatchmakingQueue>();
        matchedDate = resultDate;
        matchedTimeStart = resultStart;
        matchedTimeEnd = resultEnd;
        return result is not null;
    }

    private static bool IsCompatibleGroup(
        IReadOnlyList<MatchmakingQueue> queues,
        int geoLevel,
        DateTime now,
        out DateOnly matchedDate,
        out TimeOnly matchedTimeStart,
        out TimeOnly matchedTimeEnd)
    {
        matchedDate = default;
        matchedTimeStart = default;
        matchedTimeEnd = default;

        if (queues.Count == 0)
            return false;

        var capacity = queues[0].PlayerCount;
        var players = queues.SelectMany(q => q.QueuePlayers.Where(IsApproved)).Select(qp => qp.PlayerId).ToList();
        if (queues.Any(q => q.PlayerCount != capacity) || players.Count != capacity || players.Distinct().Count() != players.Count)
            return false;

        for (var left = 0; left < queues.Count; left++)
        for (var right = 0; right < queues.Count; right++)
            if (left != right && (queues[right].SkillLevel < queues[left].MinSkillLevel || queues[right].SkillLevel > queues[left].MaxSkillLevel))
                return false;

        if (!AreGeographicallyCompatible(queues, geoLevel))
            return false;

        return TryFindScheduleIntersection(queues, now, out matchedDate, out matchedTimeStart, out matchedTimeEnd);
    }

    private static bool AreGeographicallyCompatible(IReadOnlyList<MatchmakingQueue> queues, int geoLevel)
    {
        if (queues.Count == 1)
            return true;

        if (geoLevel == 3)
        {
            HashSet<string>? sharedVenues = null;
            foreach (var queue in queues)
            {
                if (string.IsNullOrWhiteSpace(queue.SharedVenues))
                    return false;

                var venues = queue.SharedVenues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                sharedVenues = sharedVenues is null
                    ? new HashSet<string>(venues, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(sharedVenues.Intersect(venues, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

                if (sharedVenues.Count == 0)
                    return false;
            }

            return true;
        }

        if (geoLevel == 2)
        {
            var province = NormalizeAreaForMatching(queues[0].Province);
            var ward = NormalizeAreaForMatching(queues[0].Ward);
            return province.Length > 0 && ward.Length > 0 && queues.All(q =>
                NormalizeAreaForMatching(q.Province) == province &&
                NormalizeAreaForMatching(q.Ward) == ward);
        }

        for (var left = 0; left < queues.Count; left++)
        {
            for (var right = left + 1; right < queues.Count; right++)
            {
                if (!AreBroadLocationsCompatible(queues[left], queues[right]))
                    return false;
            }
        }

        return true;
    }

    private static bool AreBroadLocationsCompatible(MatchmakingQueue left, MatchmakingQueue right)
    {
        if (left.SearchLatitude.HasValue && left.SearchLongitude.HasValue &&
            right.SearchLatitude.HasValue && right.SearchLongitude.HasValue)
        {
            var distance = DistanceKm(
                left.SearchLatitude.Value,
                left.SearchLongitude.Value,
                right.SearchLatitude.Value,
                right.SearchLongitude.Value);
            return distance <= left.SearchRadiusKm && distance <= right.SearchRadiusKm;
        }

        var cityLeft = left.Province ?? left.QueuePlayers.FirstOrDefault(qp => qp.IsHost)?.Player?.User?.City;
        var cityRight = right.Province ?? right.QueuePlayers.FirstOrDefault(qp => qp.IsHost)?.Player?.User?.City;
        return !string.IsNullOrWhiteSpace(cityLeft) &&
               !string.IsNullOrWhiteSpace(cityRight) &&
               NormalizeAreaForMatching(cityLeft) == NormalizeAreaForMatching(cityRight);
    }

    public static bool TryFindScheduleIntersection(
        IReadOnlyList<MatchmakingQueue> queues,
        DateTime now,
        out DateOnly matchedDate,
        out TimeOnly matchedTimeStart,
        out TimeOnly matchedTimeEnd)
    {
        matchedDate = default;
        matchedTimeStart = default;
        matchedTimeEnd = default;

        if (queues.Count == 0 || queues.Any(q => q.QueueSlots.Count == 0))
            return false;

        var localNow = now.Kind == DateTimeKind.Utc ? VietnamTime.FromUtc(now) : now;
        var today = DateOnly.FromDateTime(localNow);
        var currentTime = TimeOnly.FromDateTime(localNow);
        var candidateDates = Enumerable.Range(0, 63)
            .Select(today.AddDays)
            .Concat(queues.SelectMany(q => q.QueueSlots)
                .Where(s => s.SpecificDate >= today)
                .Select(s => s.SpecificDate!.Value))
            .Distinct()
            .OrderBy(date => date);

        foreach (var date in candidateDates)
        {
            var slotsByQueue = queues
                .Select(q => q.QueueSlots.Where(slot => SlotAppliesOn(slot, date)).ToList())
                .ToList();
            if (slotsByQueue.Any(slots => slots.Count == 0))
                continue;

            var possibleStartMins = slotsByQueue
                .SelectMany(slots => slots)
                .Select(s => s.TimeStart.Hour * 60 + s.TimeStart.Minute)
                .Distinct()
                .OrderBy(m => m);

            var currentMin = currentTime.Hour * 60 + currentTime.Minute;

            foreach (var startMin in possibleStartMins)
            {
                if (date == today && startMin <= currentMin)
                    continue;

                int? commonEndMin = null;
                foreach (var slots in slotsByQueue)
                {
                    var coveringSlots = slots.Where(slot => {
                        var stM = slot.TimeStart.Hour * 60 + slot.TimeStart.Minute;
                        var enM = (slot.TimeEnd == TimeOnly.MinValue && slot.TimeStart > TimeOnly.MinValue) ? 24 * 60 : slot.TimeEnd.Hour * 60 + slot.TimeEnd.Minute;
                        return stM < enM && stM <= startMin && enM > startMin;
                    }).ToList();

                    if (coveringSlots.Count == 0)
                    {
                        commonEndMin = null;
                        break;
                    }

                    var queueEndMin = coveringSlots.Max(slot => (slot.TimeEnd == TimeOnly.MinValue && slot.TimeStart > TimeOnly.MinValue) ? 24 * 60 : slot.TimeEnd.Hour * 60 + slot.TimeEnd.Minute);
                    commonEndMin = !commonEndMin.HasValue || queueEndMin < commonEndMin.Value ? queueEndMin : commonEndMin;
                }

                if (commonEndMin.HasValue && (commonEndMin.Value - startMin >= 30))
                {
                    matchedDate = date;
                    matchedTimeStart = new TimeOnly(startMin / 60, startMin % 60);
                    matchedTimeEnd = commonEndMin.Value >= 1440 ? TimeOnly.MinValue : new TimeOnly(commonEndMin.Value / 60, commonEndMin.Value % 60);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SlotAppliesOn(MatchmakingQueueSlot slot, DateOnly date)
    {
        if (slot.SpecificDate.HasValue)
            return slot.SpecificDate.Value == date;
        if (slot.DayOfWeek.HasValue)
            return slot.DayOfWeek.Value == date.DayOfWeek;
        if (slot.DayOfMonth.HasValue)
            return slot.DayOfMonth.Value == date.Day;
        return true;
    }

    private async Task<bool> ApplyMatchAsync(
        ApplicationDbContext db,
        IReadOnlyList<MatchmakingQueue> queues,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var primaryQueue = queues.FirstOrDefault(queue => queue.MatchId.HasValue) ?? queues[0];
            var playerIds = queues.SelectMany(queue => queue.QueuePlayers.Where(IsApproved))
                .Select(player => player.PlayerId).Distinct().OrderBy(id => id).ToList();
            foreach (var playerId in playerIds)
            {
                if (!await SqlServerBookingLock.AcquireAsync(
                        db, transaction, $"matchmaking-player:{playerId}", cancellationToken))
                    return false;
            }

            var candidateQueueIds = await db.MatchmakingQueues.AsNoTracking()
                .Where(queue => queue.IsActive && (!queue.IsPublic || queue.MatchId.HasValue)
                    && queue.QueuePlayers.Any(player =>
                        playerIds.Contains(player.PlayerId) && player.Status == "Approved"))
                .Select(queue => queue.MatchmakingQueueId)
                .ToListAsync(cancellationToken);
            foreach (var queueId in candidateQueueIds.OrderBy(id => id))
            {
                if (!await SqlServerBookingLock.AcquireAsync(
                        db, transaction, $"matchmaking-queue:{queueId}", cancellationToken))
                    return false;
            }

            var selectedQueueIds = queues.Select(queue => queue.MatchmakingQueueId).ToList();
            var activeSelectedCount = await db.MatchmakingQueues.AsNoTracking()
                .CountAsync(queue => selectedQueueIds.Contains(queue.MatchmakingQueueId)
                    && queue.IsActive && (!queue.IsPublic || queue.MatchId.HasValue), cancellationToken);
            if (activeSelectedCount != selectedQueueIds.Count)
            {
                await transaction.RollbackAsync(cancellationToken);
                return true;
            }

            var hostQP = primaryQueue.QueuePlayers.First(qp => qp.IsHost && IsApproved(qp));
            var hostUser = hostQP.Player.User;

            var linkedMatchId = queues.Where(queue => queue.MatchId.HasValue)
                .Select(queue => queue.MatchId!.Value)
                .Distinct()
                .SingleOrDefault();
            Match targetMatch;
            if (linkedMatchId > 0)
            {
                targetMatch = await db.Matches
                    .Include(match => match.MatchParticipants)
                    .Include(match => match.AvailabilitySlots)
                    .SingleOrDefaultAsync(match => match.MatchId == linkedMatchId, cancellationToken)
                    ?? throw new InvalidOperationException($"Linked matchmaking room {linkedMatchId} no longer exists.");
                if (targetMatch.Status != "Recruiting")
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return true;
                }

                targetMatch.AvailableDateFrom = date;
                targetMatch.AvailableDateTo = date;
                targetMatch.PreferredTimeStart = start;
                targetMatch.PreferredTimeEnd = end;
                targetMatch.Status = "ReadyToBook";
                if (!targetMatch.AvailabilitySlots.Any(slot => slot.TimeStart == start && slot.TimeEnd == end))
                {
                    targetMatch.AvailabilitySlots.Add(new MatchAvailabilitySlot
                    {
                        MatchId = targetMatch.MatchId,
                        TimeStart = start,
                        TimeEnd = end
                    });
                }
            }
            else
            {
                targetMatch = new Match
                {
                    HostPlayerId = hostQP.PlayerId,
                    MatchType = primaryQueue.MatchType,
                    MinSkillLevel = queues.Max(q => q.MinSkillLevel),
                    MaxSkillLevel = queues.Min(q => q.MaxSkillLevel),
                    MatchSkillLevel = (int)Math.Round(queues.Average(q => q.SkillLevel)),
                    RequiredPlayerCount = primaryQueue.PlayerCount,
                    Status = "ReadyToBook",
                    Origin = "Automatic",
                    Title = primaryQueue.Title,
                    Province = queues.Select(q => q.Province).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? hostUser.City ?? "Hồ Chí Minh",
                    Ward = queues.Select(q => q.Ward).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? hostUser.Commune ?? "Quận 1",
                    SharedVenues = queues.Select(q => q.SharedVenues).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                    AvailableDateFrom = date,
                    AvailableDateTo = date,
                    PreferredTimeStart = start,
                    PreferredTimeEnd = end,
                    CreatedAt = now
                };
                targetMatch.AvailabilitySlots.Add(new MatchAvailabilitySlot
                {
                    TimeStart = start,
                    TimeEnd = end
                });
                db.Matches.Add(targetMatch);
                await db.SaveChangesAsync(cancellationToken);
            }

            var allQueuePlayers = queues.SelectMany(q => q.QueuePlayers.Where(IsApproved)).ToList();
            var matchedPlayerIds = new List<int>();

            foreach (var qp in allQueuePlayers)
            {
                var existingParticipant = targetMatch.MatchParticipants
                    .FirstOrDefault(participant => participant.PlayerId == qp.PlayerId);
                if (existingParticipant is null)
                {
                    targetMatch.MatchParticipants.Add(new MatchParticipant
                    {
                        MatchId = targetMatch.MatchId,
                        PlayerId = qp.PlayerId,
                        Status = "Approved",
                        IsHost = qp.PlayerId == targetMatch.HostPlayerId,
                        RequestedAt = now,
                        RespondedAt = now
                    });
                }
                else
                {
                    existingParticipant.Status = "Approved";
                    existingParticipant.RespondedAt = now;
                }
                matchedPlayerIds.Add(qp.PlayerId);
            }

            var conversation = await db.Conversations
                .SingleOrDefaultAsync(item => item.MatchId == targetMatch.MatchId && item.ConversationType == "LobbyChat", cancellationToken);
            if (conversation is null)
            {
                conversation = new Conversation
                {
                    MatchId = targetMatch.MatchId,
                    ConversationType = "LobbyChat",
                    ConversationName = targetMatch.Title,
                    CreatedAt = now
                };
                db.Conversations.Add(conversation);
                await db.SaveChangesAsync(cancellationToken);
            }

            var existingConversationUserIds = await db.ConversationParticipants
                .Where(item => item.ConversationId == conversation.ConversationId)
                .Select(item => item.UserId)
                .ToListAsync(cancellationToken);
            foreach (var qp in allQueuePlayers)
            {
                if (existingConversationUserIds.Contains(qp.Player.UserId)) continue;
                db.ConversationParticipants.Add(new ConversationParticipant
                {
                    ConversationId = conversation.ConversationId,
                    UserId = qp.Player.UserId,
                    JoinedAt = now
                });
            }

            var tickets = await db.MatchmakingQueues
                .Include(item => item.QueuePlayers)
                .Where(item => candidateQueueIds.Contains(item.MatchmakingQueueId))
                .ToListAsync(cancellationToken);
            var linkedTicket = tickets.FirstOrDefault(ticket => ticket.MatchId == targetMatch.MatchId);
            if (linkedTicket is not null)
            {
                foreach (var queuePlayer in allQueuePlayers)
                {
                    if (linkedTicket.QueuePlayers.Any(existing => existing.PlayerId == queuePlayer.PlayerId)) continue;
                    linkedTicket.QueuePlayers.Add(new MatchmakingQueuePlayer
                    {
                        PlayerId = queuePlayer.PlayerId,
                        IsHost = queuePlayer.PlayerId == targetMatch.HostPlayerId,
                        Status = "Approved"
                    });
                }
            }
            foreach (var ticket in tickets)
            {
                if (ticket.MatchId == targetMatch.MatchId)
                {
                    ticket.IsActive = false;
                    ticket.UpdatedAt = now;
                }
                else if (ticket.IsPublic && ticket.MatchId.HasValue)
                {
                    // A public ticket owns a different room. It is not disposable just
                    // because one of its players was matched through another ticket.
                    continue;
                }
                else if (ticket.ReplayType == "None")
                {
                    // Keep the ticket (deactivated, linked to the match it fed into) instead of
                    // deleting it outright, so /status and /my-queues can still surface the
                    // resulting matchId to a client that is polling instead of listening for the
                    // realtime "Matched" event. The stale-queue sweep below reclaims this row later.
                    ticket.IsActive = false;
                    ticket.MatchId = targetMatch.MatchId;
                    ticket.UpdatedAt = now;
                }
                else
                {
                    ticket.IsActive = false;
                    ticket.UpdatedAt = now;
                }
            }

            if (_firebaseService != null && _firebaseService.IsConfigured)
            {
                foreach (var ticket in tickets)
                {
                    if (ticket.MatchId == targetMatch.MatchId)
                    {
                        _ = _firebaseService.SyncQueueAsync(ticket.MatchmakingQueueId, new
                        {
                            ticket.MatchmakingQueueId,
                            ticket.MatchId,
                            ticket.Title,
                            ticket.PlayerCount,
                            ticket.MatchType,
                            ticket.IsActive,
                            ticket.IsPublic,
                            UpdatedAt = ticket.UpdatedAt.ToString("o")
                        }, CancellationToken.None);
                    }
                    else if (ticket.IsPublic && ticket.MatchId.HasValue)
                    {
                        continue;
                    }
                    else
                    {
                        _ = _firebaseService.RemoveQueueAsync(ticket.MatchmakingQueueId, CancellationToken.None);
                    }
                }
            }

            foreach (var qp in allQueuePlayers)
            {
                var notif = new NotificationLog
                {
                    UserId = qp.Player.UserId,
                    NotificationType = "match",
                    Title = "Đã tìm thấy trận đấu!",
                    Message = $"Bạn đã được ghép thành công vào trận \"{targetMatch.Title}\".",
                    Tone = "success",
                    LinkTo = $"/matches/{targetMatch.MatchId}",
                    LinkLabel = "Xem phòng",
                    CreatedAt = DateTime.UtcNow
                };
                db.NotificationLogs.Add(notif);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // Direct in-memory realtime notification (no HTTP webhook overhead)
            _matchRealtime.Publish(targetMatch.MatchId, "Matched");
            foreach (var qp in allQueuePlayers)
            {
                var userNotif = await db.NotificationLogs
                    .Where(n => n.UserId == qp.Player.UserId)
                    .OrderByDescending(n => n.NotifId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (userNotif != null)
                {
                    _notificationRealtime.Publish(qp.Player.UserId, userNotif.NotifId, "Created");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(
                ex,
                "Failed to apply matchmaking result between queues {queueIds}.",
                string.Join(", ", queues.Select(q => q.MatchmakingQueueId)));
            return false;
        }
    }

    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var r = 6371d;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }

    private static double ToRadians(double angle) => Math.PI * angle / 180.0;

    private static string NormalizeAreaForMatching(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark
                && char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }
        return builder.ToString();
    }
}
