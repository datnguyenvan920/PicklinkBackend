using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public partial class MatchController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private readonly PlayerScheduleConflictService _playerScheduleConflict;

    public MatchController(
        ApplicationDbContext db,
        IConfiguration configuration,
        ScheduleRealtimeNotifier scheduleRealtime,
        MatchRealtimeNotifier matchRealtime,
        PlayerScheduleConflictService playerScheduleConflict)
    {
        _db = db;
        _configuration = configuration;
        _scheduleRealtime = scheduleRealtime;
        _matchRealtime = matchRealtime;
        _playerScheduleConflict = playerScheduleConflict;
    }

    /// <summary>
    /// Returns the lobby card for the currently authenticated user.
    /// Used by the Flutter home screen to populate slot 0 with real data.
    /// </summary>
    [Authorize]
    [HttpGet("lobby-me")]
    public async Task<ActionResult<LobbyMeResponse>> LobbyMe()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
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

    [Authorize]
    [HttpPost("matches")] // RESTful route
    public async Task<ActionResult> CreateMatch([FromBody] CreateMatchRequest createMatch)
    {
        // 1. Get the user
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
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
    [Authorize]
    [HttpGet("my-matches")]
    public async Task<ActionResult<List<MyMatchResponse>>> MyMatches()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
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
    [Authorize]
    [HttpGet("{matchId}/voting-status")]
    public async Task<ActionResult<MatchVotingStatusResponse>> GetVotingStatus(int matchId)
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
    [Authorize]
    [HttpPost("{matchId}/vote")]
    public async Task<ActionResult<MatchVotingStatusResponse>> Vote(int matchId, [FromBody] CastVoteRequest request)
    {
        // 1. Authenticate user and find their PlayerId
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.UserId == userId);
        if (player is null)
            return NotFound("Player profile not found.");

        // 2. Find Match and Participant
        var match = await _db.Matches
            .Include(m => m.MatchParticipants)
                .ThenInclude(mp => mp.Player)
                    .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(m => m.MatchId == matchId);

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

        await _db.SaveChangesAsync();

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

            // Set final match details
            match.Status = "Scheduled";
            var targetDateTime = DateTime.Today.Add(timeWinner.ToTimeSpan());
            if (targetDateTime < DateTime.Now)
            {
                targetDateTime = targetDateTime.AddDays(1);
            }
            match.MatchTime = targetDateTime;

            // Automatically book a court at the winning venue
            var court = await _db.Courts.FirstOrDefaultAsync(c => c.VenueId == venueWinner && c.AvailabilityStatus == "Available");
            if (court != null)
            {
                var booking = new Booking
                {
                    CourtId = court.CourtId,
                    MatchId = match.MatchId,
                    StartTime = targetDateTime,
                    EndTime = targetDateTime.AddMinutes(90),
                    Status = "Approved",
                    CreatedAt = DateTime.UtcNow
                };
                await _db.Bookings.AddAsync(booking);
            }

            await _db.SaveChangesAsync();

            // ── Team assignment (random shuffle) ────────────────────────
            var shuffled = match.MatchParticipants.OrderBy(_ => Random.Shared.Next()).ToList();
            var half = shuffled.Count / 2;
            var team1Players = shuffled.Take(half).ToList();
            var team2Players = shuffled.Skip(half).ToList();

            var teamA = new Team
            {
                TeamName = $"Team A – Match #{match.MatchId}",
                CaptainId = team1Players[0].PlayerId
            };
            var teamB = new Team
            {
                TeamName = $"Team B – Match #{match.MatchId}",
                CaptainId = team2Players[0].PlayerId
            };
            _db.Teams.AddRange(teamA, teamB);
            await _db.SaveChangesAsync(); // get generated TeamIds

            foreach (var mp in team1Players)
                _db.PlayerTeamRosters.Add(new PlayerTeamRoster { PlayerId = mp.PlayerId, TeamId = teamA.TeamId, JoinedDate = DateOnly.FromDateTime(DateTime.UtcNow) });
            foreach (var mp in team2Players)
                _db.PlayerTeamRosters.Add(new PlayerTeamRoster { PlayerId = mp.PlayerId, TeamId = teamB.TeamId, JoinedDate = DateOnly.FromDateTime(DateTime.UtcNow) });

            match.Team1Id = teamA.TeamId;
            match.Team2Id = teamB.TeamId;

            // ── Create LobbyChat conversation ───────────────────────────
            var conversation = new Conversation
            {
                MatchId = match.MatchId,
                ConversationType = "LobbyChat",
                ConversationName = $"Lobby – Match #{match.MatchId}",
                CreatedAt = DateTime.UtcNow
            };
            _db.Conversations.Add(conversation);
            await _db.SaveChangesAsync(); // get ConversationId

            // Add all match participants as conversation participants
            foreach (var mp in match.MatchParticipants)
            {
                var participantUserId = mp.Player?.UserId
                    ?? (await _db.Players.AsNoTracking().FirstAsync(p => p.PlayerId == mp.PlayerId)).UserId;

                _db.ConversationParticipants.Add(new ConversationParticipant
                {
                    ConversationId = conversation.ConversationId,
                    UserId = participantUserId,
                    JoinedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
        }

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

    // ── Match Detail ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns full match detail including teams and lobby chat conversation ID.
    /// </summary>
    [Authorize]
    [HttpGet("{matchId}/detail")]
    public async Task<ActionResult<MatchDetailResponse>> GetDetail(int matchId)
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

    // ── Lobby Chat ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns chat messages for the match's lobby conversation.
    /// </summary>
    [Authorize]
    [HttpGet("{matchId}/messages")]
    public async Task<ActionResult> GetMessages(int matchId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
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
    [Authorize]
    [HttpPost("{matchId}/messages")]
    public async Task<ActionResult> SendMessage(int matchId, [FromBody] SendMatchMessageRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
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
