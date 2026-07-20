using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.MatchmakingWorker;

public class MatchmakingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MatchmakingWorker> _logger;
    private DateTime _lastCleanupDate = DateTime.MinValue;

    public MatchmakingWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<MatchmakingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }


    private static bool IsApproved(MatchmakingQueuePlayer queuePlayer) => queuePlayer.Status == "Approved";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var scanIntervalSeconds = Math.Clamp(_configuration.GetValue("MatchmakingWorker:ScanIntervalSeconds", 10), 2, 60);
        _logger.LogInformation("MatchmakingWorker started. Scan interval: {seconds} seconds.", scanIntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(scanIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunMatchmakingScanAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during matchmaking scan.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task RunMatchmakingScanAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // 1. Clean dead/stale queue records (Inactive + Private and not modified for 10 days) - Run once per day in a separate background thread
        var todayUtc = DateTime.UtcNow.Date;
        if (todayUtc > _lastCleanupDate)
        {
            _lastCleanupDate = todayUtc; // Update immediately to prevent duplicate runs
            
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cleanupScope = _scopeFactory.CreateScope();
                    var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var tenDaysAgo = DateTime.UtcNow.AddDays(-10);
                    var staleQueues = await cleanupDb.MatchmakingQueues
                        .Where(q => !q.IsActive && !q.IsPublic && q.UpdatedAt < tenDaysAgo)
                        .ToListAsync(cancellationToken);

                    if (staleQueues.Count > 0)
                    {
                        _logger.LogInformation("Background task: Cleaning up {count} stale matchmaking queues.", staleQueues.Count);
                        cleanupDb.MatchmakingQueues.RemoveRange(staleQueues);
                        await cleanupDb.SaveChangesAsync(cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during background stale queue cleanup.");
                }
            }, cancellationToken);
        }

        // 2. Fetch active queue entries
        var queueItems = await db.MatchmakingQueues
            .Where(q => q.IsActive)
            .Include(q => q.QueueSlots)
            .Include(q => q.QueuePlayers).ThenInclude(qp => qp.Player).ThenInclude(p => p.User)
            .OrderBy(q => q.UpdatedAt) // Order by UpdatedAt so older active searchers get priority
            .ToListAsync(cancellationToken);

        if (queueItems.Count == 0)
        {
            _logger.LogInformation("Matchmaking scan: no active queues. Waiting for searchers...");
            return;
        }

        _logger.LogInformation("Scanning matchmaking queue. Active queue size: {count}. Candidates: {list}", 
            queueItems.Count, 
            string.Join(", ", queueItems.Select(q => $"#{q.MatchmakingQueueId} (Format: {q.MatchType}, Skill: {q.SkillLevel}, Wait: {(DateTime.UtcNow - q.UpdatedAt).TotalMinutes:N1}m)")));

        var grouped = queueItems.GroupBy(q => q.MatchType);

        var matchedPlayerIds = new HashSet<int>();
        foreach (var group in grouped)
        {
            var candidates = group.ToList();
            var matchedQueueIds = new HashSet<int>();
            var applyFailed = false;

            // Run matching loops in 3 geographic priority levels:
            // Level 3 = Same Venue (SharedVenue match)
            // Level 2 = Same Ward (Ward & Province match)
            // Level 1 = Broad Match (GPS Radius or Province match)
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

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

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

            var possibleStarts = slotsByQueue
                .SelectMany(slots => slots)
                .Select(slot => slot.TimeStart)
                .Distinct()
                .OrderBy(time => time);

            foreach (var start in possibleStarts)
            {
                if (date == today && start <= currentTime)
                    continue;

                TimeOnly? commonEnd = null;
                foreach (var slots in slotsByQueue)
                {
                    var coveringSlots = slots.Where(slot => slot.TimeStart <= start && slot.TimeEnd > start).ToList();
                    if (coveringSlots.Count == 0)
                    {
                        commonEnd = null;
                        break;
                    }

                    var queueEnd = coveringSlots.Max(slot => slot.TimeEnd);
                    commonEnd = !commonEnd.HasValue || queueEnd < commonEnd.Value ? queueEnd : commonEnd;
                }

                if (commonEnd.HasValue && commonEnd.Value - start >= TimeSpan.FromMinutes(90))
                {
                    matchedDate = date;
                    matchedTimeStart = start;
                    matchedTimeEnd = commonEnd.Value;
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
            var primaryQueue = queues[0];
            var playerIds = queues.SelectMany(queue => queue.QueuePlayers.Where(IsApproved))
                .Select(player => player.PlayerId).Distinct().OrderBy(id => id).ToList();
            foreach (var playerId in playerIds)
            {
                if (!await SqlServerBookingLock.AcquireAsync(
                        db, transaction, $"matchmaking-player:{playerId}", cancellationToken))
                    return false;
            }

            var candidateQueueIds = await db.MatchmakingQueues.AsNoTracking()
                .Where(queue => queue.IsActive
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
                    && queue.IsActive && queue.MatchId == null, cancellationToken);
            if (activeSelectedCount != selectedQueueIds.Count)
            {
                // ponytail: another worker already consumed this set; treat it as handled.
                await transaction.RollbackAsync(cancellationToken);
                return true;
            }

            var hostQP = primaryQueue.QueuePlayers.First(qp => qp.IsHost && IsApproved(qp));
            var hostUser = hostQP.Player.User;

            // 1. Create a brand new Match (game room)
            var targetMatch = new Match
            {
                HostPlayerId = hostQP.PlayerId,
                MatchType = primaryQueue.MatchType,
                MinSkillLevel = queues.Max(q => q.MinSkillLevel),
                MaxSkillLevel = queues.Min(q => q.MaxSkillLevel),
                MatchSkillLevel = (int)Math.Round(queues.Average(q => q.SkillLevel)),
                RequiredPlayerCount = primaryQueue.PlayerCount,
                Status = "ReadyToBook",
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

            db.Matches.Add(targetMatch);
            await db.SaveChangesAsync(cancellationToken);

            // 2. Add availability slot
            db.MatchAvailabilitySlots.Add(new MatchAvailabilitySlot
            {
                MatchId = targetMatch.MatchId,
                TimeStart = start,
                TimeEnd = end
            });

            // 3. Add all matched players to MatchParticipants
            var allQueuePlayers = queues.SelectMany(q => q.QueuePlayers.Where(IsApproved)).ToList();
            var matchedPlayerIds = new List<int>();

            foreach (var qp in allQueuePlayers)
            {
                db.MatchParticipants.Add(new MatchParticipant
                {
                    MatchId = targetMatch.MatchId,
                    PlayerId = qp.PlayerId,
                    Status = "Approved",
                    IsHost = qp.PlayerId == hostQP.PlayerId,
                    RequestedAt = now,
                    RespondedAt = now
                });
                matchedPlayerIds.Add(qp.PlayerId);
            }

            // 4. Create Lobby Chat Conversation
            var conversation = new Conversation
            {
                MatchId = targetMatch.MatchId,
                ConversationType = "LobbyChat",
                ConversationName = targetMatch.Title,
                CreatedAt = now
            };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(cancellationToken);

            // Add all players to chat conversation
            foreach (var qp in allQueuePlayers)
            {
                db.ConversationParticipants.Add(new ConversationParticipant
                {
                    ConversationId = conversation.ConversationId,
                    UserId = qp.Player.UserId,
                    JoinedAt = now
                });
            }

            // 5. Consume every active ticket owned by a matched player.
            var tickets = await db.MatchmakingQueues
                .Where(item => candidateQueueIds.Contains(item.MatchmakingQueueId))
                .ToListAsync(cancellationToken);
            foreach (var ticket in tickets)
            {
                if (ticket.ReplayType == "None")
                    db.MatchmakingQueues.Remove(ticket);
                else
                {
                    ticket.IsActive = false;
                    ticket.UpdatedAt = now;
                }
            }

            // 6. Add standard notification logs
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

            // 7. Trigger realtime updates asynchronously via webhooks to API server
            _ = Task.Run(() => NotifyRealtimeUpdatesAsync(targetMatch.MatchId, matchedPlayerIds), CancellationToken.None);

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

    private async Task NotifyRealtimeUpdatesAsync(int matchId, List<int> playerIds)
    {
        var apiServerUrl = _configuration.GetValue("MatchmakingWorker:ApiServerUrl", "http://localhost:5209")?.TrimEnd('/');
        var internalSecret = _configuration["MatchmakingWorker:InternalSecret"];
        if (string.IsNullOrEmpty(apiServerUrl) || string.IsNullOrWhiteSpace(internalSecret))
        {
            _logger.LogWarning("Realtime webhook skipped because its URL or internal secret is missing.");
            return;
        }

        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Picklink-Worker-Secret", internalSecret);
        try
        {
            // Notify match change
            var matchNotifyUrl = $"{apiServerUrl}/api/matchmaking/internal/notify-match?matchId={matchId}&action=Matched";
            await client.PostAsync(matchNotifyUrl, null);

            // Notify each matched player
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userIds = await db.Players
                .Where(p => playerIds.Contains(p.PlayerId))
                .Select(p => p.UserId)
                .ToListAsync();

            foreach (var userId in userIds)
            {
                var latestNotif = await db.NotificationLogs
                    .Where(n => n.UserId == userId)
                    .OrderByDescending(n => n.NotifId)
                    .FirstOrDefaultAsync();

                if (latestNotif is not null)
                {
                    var playerNotifyUrl = $"{apiServerUrl}/api/matchmaking/internal/notify-player?userId={userId}&notificationId={latestNotif.NotifId}&action=Created";
                    await client.PostAsync(playerNotifyUrl, null);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send realtime webhook notification to main API server.");
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
