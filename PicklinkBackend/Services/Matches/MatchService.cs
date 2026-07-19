using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Matches;

public sealed record MatchServiceDependencies(ApplicationDbContext Db, IConfiguration Configuration, ScheduleRealtimeNotifier ScheduleRealtime, MatchRealtimeNotifier MatchRealtime, NotificationService Notifications, PlayerScheduleConflictService PlayerScheduleConflict);

public partial class MatchService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private readonly NotificationService _notifications;
    private readonly PlayerScheduleConflictService _playerScheduleConflict;

    public MatchService(
        ApplicationDbContext db,
        IConfiguration configuration,
        ScheduleRealtimeNotifier scheduleRealtime,
        MatchRealtimeNotifier matchRealtime,
        NotificationService notifications,
        PlayerScheduleConflictService playerScheduleConflict)
    {
        _db = db;
        _configuration = configuration;
        _scheduleRealtime = scheduleRealtime;
        _matchRealtime = matchRealtime;
        _notifications = notifications;
        _playerScheduleConflict = playerScheduleConflict;
    }

    private int? _currentUserId;

    public void SetCurrentUserId(int? userId) => _currentUserId = userId;

    private int? CurrentUserId() => _currentUserId;

    private bool TryGetCurrentUserId(out int userId)
    {
        if (_currentUserId.HasValue)
        {
            userId = _currentUserId.Value;
            return true;
        }

        userId = 0;
        return false;
    }

    private static ServiceResult Ok(object? value = null) =>
        new(ServiceResultStatus.Success, value);


    private static ServiceResult NoContent() =>
        new(ServiceResultStatus.NoContent);

    private static ServiceResult BadRequest(object? error = null) =>
        new(ServiceResultStatus.BadRequest, Error: error);

    private static ServiceResult Unauthorized(object? error = null) =>
        new(ServiceResultStatus.Unauthorized, Error: error);

    private static ServiceResult Forbid(object? error = null) =>
        new(ServiceResultStatus.Forbidden, Error: error);

    private static ServiceResult NotFound(object? error = null) =>
        new(ServiceResultStatus.NotFound, Error: error);

    private static ServiceResult Conflict(object? error = null) =>
        new(ServiceResultStatus.Conflict, Error: error);

    private static ServiceResult StatusCode(int statusCode, object? body = null) =>
        statusCode >= 400
            ? new(ServiceResultStatus.StatusCode, Error: body, RawStatusCode: statusCode)
            : new(ServiceResultStatus.StatusCode, Value: body, RawStatusCode: statusCode);

    private static ServiceResult<T> CreatedAtAction<T>(string actionName, object routeValues, T value) =>
        new(ServiceResultStatus.Created, value, CreatedActionName: actionName, CreatedRouteValues: routeValues);
    /// <summary>
    /// Returns the lobby card for the currently authenticated user.
    /// Used by the Flutter home screen to populate slot 0 with real data.
    /// </summary>
    public async Task<ServiceResult<LobbyMeResponse>> LobbyMe()
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var user = await _db.Users
            .Include(u => u.Players)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user is null)
            return NotFound(new { message = "User not found." });

        // Use first Player profile if it exists (a user can have at most one)
        var player = user.Players.FirstOrDefault();

        var skillLevel = player?.SkillLevel ?? 0.0;
        var prestige    = player?.Prestige   ?? 0;

        return Ok(new LobbyMeResponse
        {
            UserId         = user.UserId,
            PlayerId       = player?.PlayerId,
            Username       = user.Username,
            AvatarInitials = LobbyMeResponse.InitialsFromUsername(user.Username),
            SkillLevel     = skillLevel,
            Tier           = LobbyMeResponse.TierFromSkillLevel(skillLevel),
            Prestige       = prestige,
            ProfileImageUrl = user.ProfileImageUrl,
        });
    }
    public async Task<ServiceResult> CreateMatch(CreateMatchRequest createMatch)
    {
        // 1. Get the user
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized("Invalid user token.");

        // 2. Map the data
        var newMatch = new Match
        {
            MatchType = createMatch.MatchType,
            MatchSkillLevel = createMatch.MatchSkillLevel,
            MatchTime = createMatch.MatchTime,
            Status = "Pending" // FIX: Server dictates the initial status, not the client
        };

        // 3. Save to database
        await _db.Matches.AddAsync(newMatch);
        await _db.SaveChangesAsync();

        // 4. Return 201 Created with the new object (assuming newMatch gets an Id assigned by the DB)
        return StatusCode(201, newMatch);

    }

    /// <summary>
    /// Returns the current player's recent matches for the home screen.
    /// </summary>
    public async Task<ServiceResult<List<MyMatchResponse>>> MyMatches()
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.UserId == userId);
        if (player is null)
            return Ok(new List<MyMatchResponse>());

        var matches = await _db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.PlayerId == player.PlayerId)
            .OrderByDescending(mp => mp.Match.MatchTime)
            .Take(10)
            .Select(mp => new MyMatchResponse
            {
                MatchId = mp.Match.MatchId,
                MatchType = mp.Match.MatchType,
                Status = mp.Match.Status,
                MatchTime = mp.Match.MatchTime,
                MatchSkillLevel = mp.Match.MatchSkillLevel,
                PreferredTimeStart = mp.Match.PreferredTimeStart.HasValue
                    ? mp.Match.PreferredTimeStart.Value.ToString("HH:mm")
                    : null,
                PreferredTimeEnd = mp.Match.PreferredTimeEnd.HasValue
                    ? mp.Match.PreferredTimeEnd.Value.ToString("HH:mm")
                    : null,
                VenueName = mp.Match.Bookings
                    .OrderBy(b => b.StartTime)
                    .Select(b => b.Court.Venue.VenueName)
                    .FirstOrDefault(),
                PlayerCount = mp.Match.MatchParticipants.Count
            })
            .ToListAsync();

        return Ok(matches);
    }

    /// <summary>
    /// Gets the candidate time slots, candidate venues, and current voting status for a match.
    /// </summary>
    public async Task<ServiceResult<MatchVotingStatusResponse>> GetVotingStatus(int matchId)
    {
        var match = await _db.Matches
            .Include(m => m.MatchParticipants)
                .ThenInclude(mp => mp.Player)
                    .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(m => m.MatchId == matchId);

        if (match is null)
        {
            return NotFound("Match not found.");
        }

        var response = await BuildVotingStatusResponse(match);
        return Ok(response);
    }

    /// <summary>
    /// Submits a player's vote for the match venue and start time.
    /// Resolves match automatically if all players have voted.
    /// </summary>
    public async Task<ServiceResult<MatchVotingStatusResponse>> Vote(
        int matchId,
        CastVoteRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Authenticate user and find their PlayerId
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (player is null)
            return NotFound("Player profile not found.");

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                _db,
                transaction,
                $"legacy-match-vote:{matchId}",
                cancellationToken))
        {
            return Conflict("Another vote is being processed. Please try again.");
        }

        // 2. Find Match and Participant
        var match = await _db.Matches
            .Include(m => m.MatchParticipants)
                .ThenInclude(mp => mp.Player)
                    .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(m => m.MatchId == matchId, cancellationToken);

        if (match is null)
            return NotFound("Match not found.");

        if (match.Status != "Voting")
            return BadRequest("Match voting is already closed.");

        var participant = match.MatchParticipants.FirstOrDefault(mp => mp.PlayerId == player.PlayerId);
        if (participant is null)
            return Forbid("You are not a participant of this match.");

        // 3. Validate times
        if (!TimeOnly.TryParse(request.StartTime, out var votedStart))
            return BadRequest("Invalid start time format.");

        var votedEnd = votedStart.AddMinutes(90);

        if (match.PreferredTimeStart.HasValue && votedStart < match.PreferredTimeStart.Value)
            return BadRequest("Voted start time is earlier than preferred start time limit.");

        if (match.PreferredTimeEnd.HasValue && votedEnd > match.PreferredTimeEnd.Value)
            return BadRequest("Voted duration exceeds preferred end time limit.");

        // 4. Validate venue
        var venueIds = new List<int>();
        if (!string.IsNullOrEmpty(match.SharedVenues))
        {
            venueIds = match.SharedVenues.Split(',')
                .Select(s => int.TryParse(s, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToList();
        }

        if (!venueIds.Contains(request.VenueId))
            return BadRequest("The selected venue is not one of the shared/allowed venues.");

        // 5. Update vote
        participant.VotedVenueId = request.VenueId;
        participant.VotedStartTime = votedStart;
        participant.VotedEndTime = votedEnd;

        await _db.SaveChangesAsync(cancellationToken);

        // 6. Check consensus if all participants have voted
        if (match.MatchParticipants.All(mp => mp.VotedVenueId.HasValue && mp.VotedStartTime.HasValue))
        {
            // Consensus: Tally venue votes
            var venueWinner = match.MatchParticipants
                .GroupBy(mp => mp.VotedVenueId!.Value)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key) // tie breaker: lower ID
                .Select(g => g.Key)
                .First();

            // Consensus: Tally time slot votes
            var timeWinner = match.MatchParticipants
                .GroupBy(mp => mp.VotedStartTime!.Value)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key) // tie breaker: earlier time
                .Select(g => g.Key)
                .First();

            var targetDateTime = VietnamTime.Now.Date.Add(timeWinner.ToTimeSpan());
            if (targetDateTime < VietnamTime.Now)
            {
                targetDateTime = targetDateTime.AddDays(1);
            }

            var bookingEnd = targetDateTime.AddMinutes(90);
            var now = DateTime.UtcNow;
            var courtCandidates = await _db.Courts.AsNoTracking()
                .Where(candidate =>
                    candidate.VenueId == venueWinner
                    && candidate.AvailabilityStatus == "Available"
                    && candidate.Venue.IsOpen
                    && candidate.Venue.ApprovalStatus == "Approved")
                .OrderBy(candidate => candidate.CourtId)
                .Select(candidate => new { candidate.CourtId, candidate.HourlyPrice })
                .ToListAsync(cancellationToken);

            int? selectedCourtId = null;
            decimal selectedHourlyPrice = 0;
            foreach (var candidate in courtCandidates)
            {
                if (!await SqlServerBookingLock.AcquireAsync(
                        _db,
                        transaction,
                        $"court-booking:{candidate.CourtId}",
                        cancellationToken))
                {
                    continue;
                }

                var overlaps = await _db.Bookings.AnyAsync(booking =>
                    booking.CourtId == candidate.CourtId
                    && !InactiveBookingStatuses.Contains(booking.Status)
                    && (booking.Status != "Holding" || booking.HoldExpiresAt > now)
                    && booking.StartTime < bookingEnd
                    && booking.EndTime > targetDateTime,
                    cancellationToken);
                if (overlaps) continue;

                selectedCourtId = candidate.CourtId;
                selectedHourlyPrice = candidate.HourlyPrice;
                break;
            }

            if (!selectedCourtId.HasValue)
                return Conflict("No court is available for the winning venue and time.");

            foreach (var matchParticipant in match.MatchParticipants.OrderBy(item => item.PlayerId))
            {
                if (!await SqlServerBookingLock.AcquireAsync(
                        _db,
                        transaction,
                        $"player-schedule:{matchParticipant.PlayerId}",
                        cancellationToken))
                {
                    return Conflict("A participant schedule is being updated. Please try again.");
                }

                if (await _playerScheduleConflict.HasConflictAsync(
                        matchParticipant.PlayerId,
                        targetDateTime,
                        bookingEnd,
                        excludedMatchId: match.MatchId,
                        cancellationToken: cancellationToken))
                {
                    return Conflict($"{matchParticipant.Player.User.Username} has a schedule conflict.");
                }
            }

            var totalAmount = Math.Round(
                selectedHourlyPrice * (decimal)(bookingEnd - targetDateTime).TotalHours,
                0,
                MidpointRounding.AwayFromZero);
            match.Status = "Scheduled";
            match.MatchTime = targetDateTime;
            var booking = new Booking
            {
                CourtId = selectedCourtId.Value,
                MatchId = match.MatchId,
                StartTime = targetDateTime,
                EndTime = bookingEnd,
                Status = "Approved",
                CreatedAt = now,
                HourlyPriceSnapshot = selectedHourlyPrice,
                CourtAmount = totalAmount,
                TotalAmount = totalAmount
            };
            await _db.Bookings.AddAsync(booking, cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);

            // ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ Team assignment (random shuffle) ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬
            var shuffled = match.MatchParticipants.OrderBy(_ => Random.Shared.Next()).ToList();
            var half = shuffled.Count / 2;
            var team1Players = shuffled.Take(half).ToList();
            var team2Players = shuffled.Skip(half).ToList();

            var teamA = new Team
            {
                TeamName = $"Team A ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“ Match #{match.MatchId}",
                CaptainId = team1Players[0].PlayerId
            };
            var teamB = new Team
            {
                TeamName = $"Team B ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“ Match #{match.MatchId}",
                CaptainId = team2Players[0].PlayerId
            };
            _db.Teams.AddRange(teamA, teamB);
            await _db.SaveChangesAsync(cancellationToken); // get generated TeamIds

            foreach (var mp in team1Players)
                _db.PlayerTeamRosters.Add(new PlayerTeamRoster { PlayerId = mp.PlayerId, TeamId = teamA.TeamId, JoinedDate = DateOnly.FromDateTime(DateTime.UtcNow) });
            foreach (var mp in team2Players)
                _db.PlayerTeamRosters.Add(new PlayerTeamRoster { PlayerId = mp.PlayerId, TeamId = teamB.TeamId, JoinedDate = DateOnly.FromDateTime(DateTime.UtcNow) });

            match.Team1Id = teamA.TeamId;
            match.Team2Id = teamB.TeamId;

            // ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ Create LobbyChat conversation ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬
            var conversation = new Conversation
            {
                MatchId = match.MatchId,
                ConversationType = "LobbyChat",
                ConversationName = $"Lobby ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“ Match #{match.MatchId}",
                CreatedAt = DateTime.UtcNow
            };
            _db.Conversations.Add(conversation);
            await _db.SaveChangesAsync(cancellationToken); // get ConversationId

            // Add all match participants as conversation participants
            foreach (var mp in match.MatchParticipants)
            {
                var participantUserId = mp.Player?.UserId
                    ?? (await _db.Players.AsNoTracking().FirstAsync(p => p.PlayerId == mp.PlayerId, cancellationToken)).UserId;

                _db.ConversationParticipants.Add(new ConversationParticipant
                {
                    ConversationId = conversation.ConversationId,
                    UserId = participantUserId,
                    JoinedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var response = await BuildVotingStatusResponse(match);
        return Ok(response);
    }

    private async Task<MatchVotingStatusResponse> BuildVotingStatusResponse(Match match)
    {
        // Parse shared venues
        var venueIds = new List<int>();
        if (!string.IsNullOrEmpty(match.SharedVenues))
        {
            venueIds = match.SharedVenues.Split(',')
                .Select(s => int.TryParse(s, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToList();
        }

        var candidateVenues = await _db.Venues
            .Where(v => venueIds.Contains(v.VenueId)
                && v.ApprovalStatus == "Approved"
                && v.IsOpen)
            .Select(v => new CandidateVenueDto
            {
                VenueId = v.VenueId,
                VenueName = v.VenueName,
                Address = v.Address
            })
            .ToListAsync();

        // Generate candidate time slots: 90-minute duration with 30-minute steps
        var candidateSlots = new List<CandidateSlotDto>();
        if (match.PreferredTimeStart.HasValue && match.PreferredTimeEnd.HasValue)
        {
            var start = match.PreferredTimeStart.Value;
            var end = match.PreferredTimeEnd.Value;
            var currentStart = start;
            var duration = TimeSpan.FromMinutes(90);
            var step = TimeSpan.FromMinutes(30);

            while (currentStart.Add(duration) <= end && currentStart >= start)
            {
                var currentEnd = currentStart.Add(duration);
                candidateSlots.Add(new CandidateSlotDto
                {
                    Start = currentStart.ToString("HH:mm:ss"),
                    End = currentEnd.ToString("HH:mm:ss")
                });
                currentStart = currentStart.Add(step);
                if (currentStart == TimeOnly.MinValue) break; // prevent infinite loop on overflow
            }
        }

        // Build vote status list
        var votes = match.MatchParticipants.Select(mp => new ParticipantVoteDto
        {
            PlayerId = mp.PlayerId,
            PlayerName = mp.Player?.User?.Username ?? $"Player {mp.PlayerId}",
            PlayerProfilePictureUrl = mp.Player?.User?.ProfileImageUrl,
            VotedVenueId = mp.VotedVenueId,
            VotedStartTime = mp.VotedStartTime?.ToString("HH:mm:ss"),
            VotedEndTime = mp.VotedEndTime?.ToString("HH:mm:ss")
        }).ToList();

        return new MatchVotingStatusResponse
        {
            MatchId = match.MatchId,
            Status = match.Status,
            PreferredTimeStart = match.PreferredTimeStart?.ToString("HH:mm:ss") ?? string.Empty,
            PreferredTimeEnd = match.PreferredTimeEnd?.ToString("HH:mm:ss") ?? string.Empty,
            CandidateSlots = candidateSlots,
            CandidateVenues = candidateVenues,
            Votes = votes
        };
    }

    // ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ Match Detail ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬

    /// <summary>
    /// Returns full match detail including teams and lobby chat conversation ID.
    /// </summary>
    public async Task<ServiceResult<MatchDetailResponse>> GetDetail(int matchId)
    {
        var match = await _db.Matches
            .AsNoTracking()
            .Include(m => m.Bookings).ThenInclude(b => b.Court).ThenInclude(c => c.Venue)
            .Include(m => m.Team1).ThenInclude(t => t!.PlayerTeamRosters).ThenInclude(r => r.Player).ThenInclude(p => p.User)
            .Include(m => m.Team2).ThenInclude(t => t!.PlayerTeamRosters).ThenInclude(r => r.Player).ThenInclude(p => p.User)
            .Include(m => m.Conversations)
            .FirstOrDefaultAsync(m => m.MatchId == matchId);

        if (match is null)
            return NotFound("Match not found.");

        var booking = match.Bookings.OrderBy(b => b.StartTime).FirstOrDefault();
        var venue = booking?.Court?.Venue;
        var lobbyChat = match.Conversations.FirstOrDefault(c => c.ConversationType == "LobbyChat");

        return Ok(new MatchDetailResponse
        {
            MatchId = match.MatchId,
            MatchType = match.MatchType,
            Status = match.Status,
            MatchTime = match.MatchTime,
            VenueName = venue?.VenueName,
            VenueAddress = venue?.Address,
            VenueRating = venue?.OverallRating,
            VenueOpenTime = venue?.OpenTime.ToString("HH:mm"),
            VenueCloseTime = venue?.CloseTime.ToString("HH:mm"),
            VenuePhone = venue?.PhoneNumber,
            VenueLatitude = venue?.Latitude,
            VenueLongitude = venue?.Longitude,
            CourtNumber = booking?.Court?.CourtNumber,
            ConversationId = lobbyChat?.ConversationId,
            Team1 = BuildTeamDto(match.Team1),
            Team2 = BuildTeamDto(match.Team2),
        });
    }

    private static TeamDetailDto? BuildTeamDto(Team? team)
    {
        if (team is null) return null;
        return new TeamDetailDto
        {
            TeamId = team.TeamId,
            TeamName = team.TeamName,
            Players = team.PlayerTeamRosters.Select(r => new TeamPlayerDto
            {
                PlayerId = r.PlayerId,
                PlayerName = r.Player?.User?.Username ?? $"Player {r.PlayerId}",
                AvatarUrl = r.Player?.User?.ProfileImageUrl,
            }).ToList()
        };
    }

    // ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ Lobby Chat ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬

    /// <summary>
    /// Returns chat messages for the match's lobby conversation.
    /// </summary>
    public async Task<ServiceResult> GetMessages(int matchId)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var conversation = await _db.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.MatchId == matchId && c.ConversationType == "LobbyChat");

        if (conversation is null)
            return Ok(Array.Empty<object>());

        // Verify user is a participant
        var isParticipant = await _db.ConversationParticipants
            .AnyAsync(cp => cp.ConversationId == conversation.ConversationId && cp.UserId == userId);
        if (!isParticipant)
            return Forbid();

        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversation.ConversationId && !m.IsDeleted)
            .OrderBy(m => m.SentAt)
            .Take(200)
            .Select(m => new
            {
                m.MessageId,
                m.ConversationId,
                m.SenderId,
                SenderName = m.Sender.Username,
                SenderAvatarUrl = m.Sender.ProfileImageUrl,
                m.Content,
                m.MessageType,
                m.SentAt,
                IsMine = m.SenderId == userId
            })
            .ToListAsync();

        return Ok(messages);
    }

    /// <summary>
    /// Sends a message to the match's lobby conversation.
    /// </summary>
    public async Task<ServiceResult> SendMessage(int matchId, SendMatchMessageRequest request)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Message content is required.");

        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.MatchId == matchId && c.ConversationType == "LobbyChat");

        if (conversation is null)
            return NotFound("No lobby chat for this match.");

        var isParticipant = await _db.ConversationParticipants
            .AnyAsync(cp => cp.ConversationId == conversation.ConversationId && cp.UserId == userId);
        if (!isParticipant)
            return Forbid();

        var now = DateTime.UtcNow;
        var message = new Message
        {
            ConversationId = conversation.ConversationId,
            SenderId = userId,
            Content = request.Content.Trim(),
            MessageType = "Text",
            SentAt = now,
            IsDeleted = false
        };

        conversation.LastMessageAt = now;
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        var sender = await _db.Users.AsNoTracking().FirstAsync(u => u.UserId == userId);
        _matchRealtime.Publish(matchId, "MessageSent");

        return Ok(new
        {
            message.MessageId,
            message.ConversationId,
            message.SenderId,
            SenderName = sender.Username,
            SenderAvatarUrl = sender.ProfileImageUrl,
            message.Content,
            message.MessageType,
            message.SentAt,
            IsMine = true
        });
    }
}
