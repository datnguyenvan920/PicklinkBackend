using System;
using System.Collections.Concurrent;
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

        if (queueItems.Count < 2)
        {
            _logger.LogInformation("Matchmaking scan: active queue size is {count} (less than 2). Waiting for more searchers...", queueItems.Count);
            return;
        }

        _logger.LogInformation("Scanning matchmaking queue. Active queue size: {count}. Candidates: {list}", 
            queueItems.Count, 
            string.Join(", ", queueItems.Select(q => $"#{q.MatchmakingQueueId} (Format: {q.MatchType}, Skill: {q.SkillLevel}, Wait: {(DateTime.UtcNow - q.UpdatedAt).TotalMinutes:N1}m)")));

        var grouped = queueItems.GroupBy(q => q.MatchType);

        foreach (var group in grouped)
        {
            var candidates = group.ToList();

            // Run pairing loops in 3 geographic priority levels:
            // Level 3 = Same Venue (SharedVenue match)
            // Level 2 = Same Ward (Ward & Province match)
            // Level 1 = Broad Match (GPS Radius or Province match)
            for (int geoLevel = 3; geoLevel >= 1; geoLevel--)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var a = candidates[i];
                    if (a.MatchmakingQueueId == 0) continue;

                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        var b = candidates[j];
                        if (b.MatchmakingQueueId == 0) continue;

                        if (AreCompatible(a, b, geoLevel, out var matchedDate, out var matchedTimeStart, out var matchedTimeEnd))
                        {
                            _logger.LogInformation("Match found at GeoLevel {level} between Queue {idA} and Queue {idB} on {date} at {start}-{end}.",
                                geoLevel, a.MatchmakingQueueId, b.MatchmakingQueueId, matchedDate, matchedTimeStart, matchedTimeEnd);

                            var success = await ApplyMatchAsync(db, a, b, matchedDate, matchedTimeStart, matchedTimeEnd, cancellationToken);
                            if (success)
                            {
                                a.MatchmakingQueueId = 0;
                                b.MatchmakingQueueId = 0;
                                break;
                            }
                        }
                    }
                }
            }
        }
    }

    private bool AreCompatible(
        MatchmakingQueue a,
        MatchmakingQueue b,
        int geoLevel,
        out DateOnly matchedDate,
        out TimeOnly matchedTimeStart,
        out TimeOnly matchedTimeEnd)
    {
        matchedDate = default;
        matchedTimeStart = default;
        matchedTimeEnd = default;

        _logger.LogInformation("Evaluating compatibility: Queue #{idA} vs Queue #{idB} (GeoLevel {level})", a.MatchmakingQueueId, b.MatchmakingQueueId, geoLevel);

        // 1. Must not contain overlapping players in both queue tickets
        if (a.QueuePlayers.Any(qpA => b.QueuePlayers.Any(qpB => qpA.PlayerId == qpB.PlayerId)))
        {
            _logger.LogInformation("  -> Reject: Overlapping players found in both tickets.");
            return false;
        }

        // 2. Capacity Check
        var capacity = a.MatchType == "1vs1" ? 2 : 4;
        if (a.QueuePlayers.Count + b.QueuePlayers.Count != capacity)
        {
            _logger.LogInformation("  -> Reject: Combined player count does not equal the required match type capacity ({aCount} + {bCount} != {capacity}).", a.QueuePlayers.Count, b.QueuePlayers.Count, capacity);
            return false;
        }

        // 3. Prioritized Geographic Location Check
        if (geoLevel == 3)
        {
            // Level 3: SharedVenue Match
            if (string.IsNullOrEmpty(a.SharedVenues) || string.IsNullOrEmpty(b.SharedVenues))
            {
                _logger.LogInformation("  -> Reject: One of the tickets does not specify preferred venues.");
                return false;
            }
            var listA = a.SharedVenues.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).ToList();
            var listB = b.SharedVenues.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).ToList();
            if (!listA.Intersect(listB).Any())
            {
                _logger.LogInformation("  -> Reject: Preferred venues do not intersect (Venues: [{listA}] vs [{listB}]).", string.Join(",", listA), string.Join(",", listB));
                return false;
            }
        }
        else if (geoLevel == 2)
        {
            // Level 2: Same Province AND same Ward
            if (string.IsNullOrEmpty(a.Province) || string.IsNullOrEmpty(b.Province) ||
                string.IsNullOrEmpty(a.Ward) || string.IsNullOrEmpty(b.Ward))
            {
                _logger.LogInformation("  -> Reject: Missing province or ward details.");
                return false;
            }
            if (NormalizeAreaForMatching(a.Province) != NormalizeAreaForMatching(b.Province) ||
                NormalizeAreaForMatching(a.Ward) != NormalizeAreaForMatching(b.Ward))
            {
                _logger.LogInformation("  -> Reject: Area mismatch (Ward: {wardA}/{provA} vs {wardB}/{provB}).", a.Ward, a.Province, b.Ward, b.Province);
                return false;
            }
        }
        else // geoLevel == 1
        {
            // Level 1: Broad Match (GPS distance check or fallback to same Province)
            if (a.SearchLatitude.HasValue && a.SearchLongitude.HasValue && b.SearchLatitude.HasValue && b.SearchLongitude.HasValue)
            {
                var distance = DistanceKm(a.SearchLatitude.Value, a.SearchLongitude.Value, b.SearchLatitude.Value, b.SearchLongitude.Value);
                if (distance > a.SearchRadiusKm || distance > b.SearchRadiusKm)
                {
                    _logger.LogInformation("  -> Reject: Distance limits exceeded (Distance: {dist:N2}km > Radii: {radA}km / {radB}km).", distance, a.SearchRadiusKm, b.SearchRadiusKm);
                    return false;
                }
            }
            else
            {
                // Fallback: Province match of the host player's registered city
                var cityA = a.Province ?? a.QueuePlayers.FirstOrDefault(qp => qp.IsHost)?.Player?.User?.City;
                var cityB = b.Province ?? b.QueuePlayers.FirstOrDefault(qp => qp.IsHost)?.Player?.User?.City;
                if (string.IsNullOrEmpty(cityA) || string.IsNullOrEmpty(cityB))
                {
                    _logger.LogInformation("  -> Reject: Location info missing for fallback city check.");
                    return false;
                }

                if (NormalizeAreaForMatching(cityA) != NormalizeAreaForMatching(cityB))
                {
                    _logger.LogInformation("  -> Reject: Fallback city mismatch ({cityA} vs {cityB}).", cityA, cityB);
                    return false;
                }
            }
        }

        // 4. Dynamic Skill Level Check (Allowed skill gap expands as wait time increases)
        var ageA = DateTime.UtcNow - a.UpdatedAt;
        var ageB = DateTime.UtcNow - b.UpdatedAt;
        var maxAllowedGap = Math.Max(GetAllowedSkillGap(ageA), GetAllowedSkillGap(ageB));

        if (Math.Abs(a.SkillLevel - b.SkillLevel) > maxAllowedGap)
        {
            _logger.LogInformation("  -> Reject: Skill gap too wide (Gap: {gap} > Max Allowed: {maxGap}. Wait ages: {ageA:N1}m / {ageB:N1}m).", Math.Abs(a.SkillLevel - b.SkillLevel), maxAllowedGap, ageA.TotalMinutes, ageB.TotalMinutes);
            return false;
        }

        // 5. Schedule / Slots Overlap Check
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var slotA in a.QueueSlots)
        {
            foreach (var slotB in b.QueueSlots)
            {
                DateOnly? dateCandidate = null;

                if (slotA.SpecificDate.HasValue && slotB.SpecificDate.HasValue)
                {
                    if (slotA.SpecificDate.Value == slotB.SpecificDate.Value)
                        dateCandidate = slotA.SpecificDate.Value;
                }
                else if (slotA.DayOfWeek.HasValue && slotB.DayOfWeek.HasValue)
                {
                    if (slotA.DayOfWeek.Value == slotB.DayOfWeek.Value)
                        dateCandidate = GetNextDateForDayOfWeek(today, slotA.DayOfWeek.Value);
                }
                else if (slotA.SpecificDate.HasValue && slotB.DayOfWeek.HasValue)
                {
                    if (slotA.SpecificDate.Value.DayOfWeek == slotB.DayOfWeek.Value)
                        dateCandidate = slotA.SpecificDate.Value;
                }
                else if (slotA.DayOfWeek.HasValue && slotB.SpecificDate.HasValue)
                {
                    if (slotB.SpecificDate.Value.DayOfWeek == slotA.DayOfWeek.Value)
                        dateCandidate = slotB.SpecificDate.Value;
                }
                else if (slotA.DayOfMonth.HasValue && slotB.DayOfMonth.HasValue)
                {
                    if (slotA.DayOfMonth.Value == slotB.DayOfMonth.Value)
                        dateCandidate = GetNextDateForDayOfMonth(today, slotA.DayOfMonth.Value);
                }
                else if (slotA.SpecificDate.HasValue && slotB.DayOfMonth.HasValue)
                {
                    if (slotA.SpecificDate.Value.Day == slotB.DayOfMonth.Value)
                        dateCandidate = slotA.SpecificDate.Value;
                }
                else if (slotA.DayOfMonth.HasValue && slotB.SpecificDate.HasValue)
                {
                    if (slotB.SpecificDate.Value.Day == slotA.DayOfMonth.Value)
                        dateCandidate = slotB.SpecificDate.Value;
                }
                else if (slotA.DayOfWeek == null && slotA.SpecificDate == null && slotA.DayOfMonth == null &&
                         slotB.DayOfWeek == null && slotB.SpecificDate == null && slotB.DayOfMonth == null)
                {
                    // Both are Daily (repeat every day) -> match them for today!
                    dateCandidate = today;
                }
                else if (slotA.DayOfWeek == null && slotA.SpecificDate == null && slotA.DayOfMonth == null)
                {
                    dateCandidate = slotB.SpecificDate ?? GetNextDateForDayOfWeek(today, slotB.DayOfWeek ?? DayOfWeek.Monday);
                }
                else if (slotB.DayOfWeek == null && slotB.SpecificDate == null && slotB.DayOfMonth == null)
                {
                    dateCandidate = slotA.SpecificDate ?? GetNextDateForDayOfWeek(today, slotA.DayOfWeek ?? DayOfWeek.Monday);
                }

                if (dateCandidate.HasValue && dateCandidate.Value >= today)
                {
                    var start = slotA.TimeStart > slotB.TimeStart ? slotA.TimeStart : slotB.TimeStart;
                    var end = slotA.TimeEnd < slotB.TimeEnd ? slotA.TimeEnd : slotB.TimeEnd;

                    if (end > start && (end - start).TotalMinutes >= 90)
                    {
                        matchedDate = dateCandidate.Value;
                        matchedTimeStart = start;
                        matchedTimeEnd = end;
                        _logger.LogInformation("  -> SUCCESS: Match compatible! Date: {date}, Slot: {start} - {end}.", matchedDate, matchedTimeStart, matchedTimeEnd);
                        return true;
                    }
                }
            }
        }

        _logger.LogInformation("  -> Reject: No overlapping schedule slot of at least 90 minutes found.");
        return false;
    }

    private static double GetAllowedSkillGap(TimeSpan waitTime)
    {
        if (waitTime.TotalMinutes >= 3) return 3.0;
        if (waitTime.TotalMinutes >= 2) return 2.5;
        if (waitTime.TotalMinutes >= 1) return 2.0;
        return 1.5; // Base skill level gap threshold
    }

    private async Task<bool> ApplyMatchAsync(
        ApplicationDbContext db,
        MatchmakingQueue a,
        MatchmakingQueue b,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var hostQP = a.QueuePlayers.First(qp => qp.IsHost);
            var hostUser = hostQP.Player.User;

            // 1. Create a brand new Match (game room)
            var targetMatch = new Match
            {
                HostPlayerId = hostQP.PlayerId,
                MatchType = a.MatchType,
                MinSkillLevel = Math.Min(a.SkillLevel, b.SkillLevel),
                MaxSkillLevel = Math.Max(a.SkillLevel, b.SkillLevel),
                MatchSkillLevel = (a.SkillLevel + b.SkillLevel) / 2,
                RequiredPlayerCount = a.MatchType == "1vs1" ? 2 : 4,
                Status = "ReadyToBook",
                Title = $"Ghép cặp {a.MatchType} - {date:dd/MM/yyyy}",
                Province = a.Province ?? b.Province ?? hostUser.City ?? "Hồ Chí Minh",
                Ward = a.Ward ?? b.Ward ?? hostUser.Commune ?? "Quận 1",
                SharedVenues = a.SharedVenues ?? b.SharedVenues,
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
            var allQueuePlayers = a.QueuePlayers.Concat(b.QueuePlayers).ToList();
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

            // 5. Handle Queue Records Cleanup / Pause
            foreach (var ticket in new[] { a, b })
            {
                var ticketFromDb = await db.MatchmakingQueues.FindAsync(new object[] { ticket.MatchmakingQueueId }, cancellationToken);
                if (ticketFromDb is not null)
                {
                    if (ticketFromDb.ReplayType == "None")
                    {
                        // Delete one-off tickets (cascade deletes slots, players, queue chat)
                        db.MatchmakingQueues.Remove(ticketFromDb);
                    }
                    else
                    {
                        // Pause recurring tickets
                        ticketFromDb.IsActive = false;
                        ticketFromDb.UpdatedAt = now;
                    }
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
            _logger.LogError(ex, "Failed to apply matchmaking result between queue {a} and {b}.", a.MatchmakingQueueId, b.MatchmakingQueueId);
            return false;
        }
    }

    private async Task NotifyRealtimeUpdatesAsync(int matchId, List<int> playerIds)
    {
        var apiServerUrl = _configuration.GetValue("MatchmakingWorker:ApiServerUrl", "http://localhost:5209")?.TrimEnd('/');
        if (string.IsNullOrEmpty(apiServerUrl)) return;

        using var client = _httpClientFactory.CreateClient();
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

    private static DateOnly GetNextDateForDayOfWeek(DateOnly current, DayOfWeek dow)
    {
        for (int i = 0; i < 7; i++)
        {
            var date = current.AddDays(i);
            if (date.DayOfWeek == dow) return date;
        }
        return current;
    }

    private static DateOnly GetNextDateForDayOfMonth(DateOnly current, int dom)
    {
        for (int i = 0; i < 32; i++)
        {
            var date = current.AddDays(i);
            if (date.Day == dom) return date;
        }
        return current;
    }

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
