using PicklinkMatching.DTO;
using System.Collections.Concurrent;
using System.Text;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace PicklinkMatching.Services
{
    /// <summary>
    /// In-memory matchmaking service (singleton lifetime).
    ///
    /// Two lobbies are matched when ALL of the following hold:
    ///   1. Same LobbyType (e.g. both "normal" or both "ranked").
    ///   2. Same LobbySize  (total players in the final match: 2 or 4).
    ///   3. Their time windows overlap:
    ///        lobby1.PreferredTimeStart ≤ lobby2.PreferredTimeEnd  AND
    ///        lobby2.PreferredTimeStart ≤ lobby1.PreferredTimeEnd
    ///   4. They share at least one common venue across all players.
    ///
    /// On a successful match the two lobbies are merged into a single result Lobby
    /// and stored so both callers can retrieve it by their respective QueueIds.
    /// </summary>
    public class MatchmakingService : IMatchmakingService
    {
        private readonly ILogger<MatchmakingService> _logger;

        // Lobbies waiting for a partner: queueId → Lobby
        private readonly ConcurrentDictionary<Guid, Lobby> _queue = new();

        // Completed matches: queueId → merged Lobby (both original queueIds map here)
        private readonly ConcurrentDictionary<Guid, Lobby> _results = new();

        // Protects the scan-and-match critical section so two concurrent requests
        // cannot simultaneously pick the same waiting lobby as a match.
        private readonly SemaphoreSlim _matchLock = new(1, 1);

        private readonly HttpClient _httpClient;
        private readonly string _backendBaseUrl;

        public MatchmakingService(
            ILogger<MatchmakingService> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _backendBaseUrl = (configuration["BackendApi:BaseUrl"] ?? "http://localhost:5209").TrimEnd('/');
        }

        public async Task<Guid> EnqueueAsync(Lobby lobby)
        {
            // Add to the waiting queue first.
            _queue[lobby.QueueId] = lobby;

            LogLobbyReceived(lobby);
            LogQueueState();

            // Try to find a compatible partner under a lock to avoid race conditions.
            await _matchLock.WaitAsync();
            try
            {
                await TryMatchAsync(lobby);
            }
            finally
            {
                _matchLock.Release();
            }

            return lobby.QueueId;
        }

        public Task<Lobby?> GetMatchAsync(Guid queueId)
        {
            _results.TryGetValue(queueId, out var result);
            return Task.FromResult(result);
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private async Task TryMatchAsync(Lobby incoming)
        {
            // The lobby might already have been matched while we were acquiring the lock.
            if (!_queue.ContainsKey(incoming.QueueId))
                return;

            _logger.LogInformation("  [Matchmaking] Scanning queue for a compatible partner for {QueueId}...", incoming.QueueId);

            foreach (var (candidateId, candidate) in _queue)
            {
                // Don't match the lobby with itself.
                if (candidateId == incoming.QueueId)
                    continue;

                _logger.LogInformation("  [Matchmaking] Checking candidate {CandidateId}...", candidateId);

                if (AreCompatible(incoming, candidate))
                {
                    // Remove both from the waiting queue.
                    _queue.TryRemove(incoming.QueueId, out _);
                    _queue.TryRemove(candidateId, out _);

                    // Build the merged result lobby.
                    var matched = MergeLobbies(incoming, candidate);

                    // Call PicklinkBackend to initialize the match in SQL DB
                    try
                    {
                        var backendUrl = $"{_backendBaseUrl}/api/match/init-from-lobby";
                        var payload = new
                        {
                            lobbyType = matched.LobbyType,
                            preferredTimeStart = matched.PreferredTimeStart.ToString("HH:mm:ss"),
                            preferredTimeEnd = matched.PreferredTimeEnd.ToString("HH:mm:ss"),
                            sharedVenues = matched.SharedVenues.ToList(),
                            playerIds = matched.Players.Select(p => p.PlayerId).ToList()
                        };

                        _logger.LogInformation("  [Matchmaking] Initializing match on backend. URL: {Url}", backendUrl);
                        var response = await _httpClient.PostAsJsonAsync(backendUrl, payload);
                        if (response.IsSuccessStatusCode)
                        {
                            var resultObj = await response.Content.ReadFromJsonAsync<InitMatchResponse>();
                            if (resultObj != null)
                            {
                                matched.MatchId = resultObj.MatchId;
                                _logger.LogInformation("  [Matchmaking] Backend match initialized successfully. MatchId: {MatchId}", matched.MatchId);
                            }
                        }
                        else
                        {
                            var errContent = await response.Content.ReadAsStringAsync();
                            _logger.LogError("  [Matchmaking] Failed to initialize match on backend. Status: {Status}, Error: {Error}", response.StatusCode, errContent);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "  [Matchmaking] Exception while calling backend to initialize match.");
                    }

                    // Store the result under BOTH original queue IDs so each caller can retrieve it.
                    _results[incoming.QueueId] = matched;
                    _results[candidateId]      = matched;

                    LogMatchFound(incoming, candidate, matched);
                    LogQueueState();

                    return; // Done – one match per call is enough.
                }
                else
                {
                    LogIncompatibleReason(incoming, candidate);
                }
            }

            _logger.LogInformation("  [Matchmaking] No compatible partner found yet. Lobby {QueueId} remains in queue.", incoming.QueueId);
        }

        private static bool AreCompatible(Lobby a, Lobby b)
        {
            // 1. Same type and size.
            if (!string.Equals(a.LobbyType, b.LobbyType, StringComparison.OrdinalIgnoreCase))
                return false;

            if (a.LobbySize != b.LobbySize)
                return false;

            // 2. Time windows must overlap by at least 1.5 hours (90 minutes).
            var overlapStart = a.PreferredTimeStart > b.PreferredTimeStart ? a.PreferredTimeStart : b.PreferredTimeStart;
            var overlapEnd = a.PreferredTimeEnd < b.PreferredTimeEnd ? a.PreferredTimeEnd : b.PreferredTimeEnd;
            if (overlapEnd <= overlapStart || (overlapEnd - overlapStart) < TimeSpan.FromMinutes(90))
                return false;

            // 3. At least one shared venue.
            var sharedVenues = a.SharedVenues.Intersect(b.SharedVenues);
            if (!sharedVenues.Any())
                return false;

            // 4. Combined player count must not exceed LobbySize.
            if (a.Players.Count + b.Players.Count > a.LobbySize)
                return false;

            return true;
        }

        private static Lobby MergeLobbies(Lobby a, Lobby b)
        {
            return new Lobby
            {
                // Keep one of the original QueueIds as the "canonical" id of the merged lobby.
                QueueId   = a.QueueId,
                MatchedAt = DateTime.UtcNow,
                LobbyType = a.LobbyType,
                LobbySize = a.LobbySize,
                Players   = a.Players.Concat(b.Players).ToList()
            };
        }

        // ── Console logging helpers ──────────────────────────────────────────

        private void LogLobbyReceived(Lobby lobby)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("╔══════════════════════════════════════════════════╗");
            sb.AppendLine("║           LOBBY RECEIVED – QUEUED                ║");
            sb.AppendLine("╠══════════════════════════════════════════════════╣");
            sb.AppendLine($"║  QueueId  : {lobby.QueueId}");
            sb.AppendLine($"║  Type     : {lobby.LobbyType}");
            sb.AppendLine($"║  Size     : {lobby.LobbySize} players (total match)");
            sb.AppendLine($"║  TimeStart: {lobby.PreferredTimeStart}");
            sb.AppendLine($"║  TimeEnd  : {lobby.PreferredTimeEnd}");
            sb.AppendLine($"║  AvgSkill : {lobby.AverageSkill:F2}");
            sb.AppendLine($"║  Venues   : [{string.Join(", ", lobby.SharedVenues)}]");
            sb.AppendLine("║  Players  :");
            foreach (var p in lobby.Players)
                sb.AppendLine($"║    → [{p.PlayerId}] {p.PlayerName,-20} Skill:{p.PlayerSkill,5:F1}  Time:{p.PreferredTimeStart}-{p.PreferredTimeEnd}  Venues:[{string.Join(",", p.PrefferedVenue ?? new())}]");
            sb.AppendLine("╚══════════════════════════════════════════════════╝");

            _logger.LogInformation("{Message}", sb.ToString());
        }

        private void LogQueueState()
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"┌─ QUEUE STATE ({_queue.Count} lobby/lobbies waiting) ─────────────────");
            foreach (var (id, lobby) in _queue)
            {
                sb.AppendLine($"│  [{id}]  Type:{lobby.LobbyType}  Size:{lobby.LobbySize}  Players:{lobby.Players.Count}  Time:{lobby.PreferredTimeStart}-{lobby.PreferredTimeEnd}");
                foreach (var p in lobby.Players)
                    sb.AppendLine($"│      · {p.PlayerName} (skill {p.PlayerSkill:F1})");
            }
            if (_queue.IsEmpty)
                sb.AppendLine("│  (empty)");
            sb.AppendLine("└──────────────────────────────────────────────────");

            _logger.LogInformation("{Message}", sb.ToString());
        }

        private void LogMatchFound(Lobby a, Lobby b, Lobby merged)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★");
            sb.AppendLine("★           MATCH FOUND!                           ★");
            sb.AppendLine("★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★");
            sb.AppendLine($"  Lobby A  QueueId : {a.QueueId}");
            sb.AppendLine($"  Lobby B  QueueId : {b.QueueId}");
            sb.AppendLine($"  MatchedAt        : {merged.MatchedAt:u}");
            sb.AppendLine($"  Type / Size      : {merged.LobbyType} / {merged.LobbySize} players");
            sb.AppendLine($"  Shared Venues    : [{string.Join(", ", merged.SharedVenues)}]");
            sb.AppendLine($"  Combined AvgSkill: {merged.AverageSkill:F2}");
            sb.AppendLine("  Final Roster:");
            foreach (var p in merged.Players)
                sb.AppendLine($"    → [{p.PlayerId}] {p.PlayerName,-20} Skill:{p.PlayerSkill,5:F1}  Venues:[{string.Join(",", p.PrefferedVenue ?? new())}]");
            sb.AppendLine("★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★");

            _logger.LogInformation("{Message}", sb.ToString());
        }

        private void LogIncompatibleReason(Lobby incoming, Lobby candidate)
        {
            var reasons = new List<string>();

            if (!string.Equals(incoming.LobbyType, candidate.LobbyType, StringComparison.OrdinalIgnoreCase))
                reasons.Add($"LobbyType mismatch ({incoming.LobbyType} vs {candidate.LobbyType})");

            if (incoming.LobbySize != candidate.LobbySize)
                reasons.Add($"LobbySize mismatch ({incoming.LobbySize} vs {candidate.LobbySize})");

            if (incoming.PreferredTimeStart > candidate.PreferredTimeEnd || candidate.PreferredTimeStart > incoming.PreferredTimeEnd)
                reasons.Add($"Time windows don't overlap ({incoming.PreferredTimeStart}-{incoming.PreferredTimeEnd} vs {candidate.PreferredTimeStart}-{candidate.PreferredTimeEnd})");

            if (!incoming.SharedVenues.Intersect(candidate.SharedVenues).Any())
                reasons.Add($"No shared venues ([{string.Join(",", incoming.SharedVenues)}] vs [{string.Join(",", candidate.SharedVenues)}])");

            if (incoming.Players.Count + candidate.Players.Count > incoming.LobbySize)
                reasons.Add($"Too many combined players ({incoming.Players.Count + candidate.Players.Count} > {incoming.LobbySize})");

            _logger.LogInformation("  [Matchmaking] ✗ Not compatible with {CandidateId}: {Reasons}",
                candidate.QueueId, string.Join(" | ", reasons));
        }
    }

    public class InitMatchResponse
    {
        public int MatchId { get; set; }
    }
}
