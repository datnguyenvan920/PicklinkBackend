using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public MatchController(ApplicationDbContext db)
    {
        _db = db;
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
    /// Initializes a match with "Voting" status when a match is found by the matchmaker.
    /// </summary>
    [HttpPost("init-from-lobby")]
    public async Task<ActionResult> InitFromLobby([FromBody] InitMatchFromLobbyRequest request)
    {
        // 1. Validate times
        if (!TimeOnly.TryParse(request.PreferredTimeStart, out var timeStart) ||
            !TimeOnly.TryParse(request.PreferredTimeEnd, out var timeEnd))
        {
            return BadRequest("Invalid time formats.");
        }

        // 2. Look up players to calculate average skill level
        var players = await _db.Players.Where(p => request.PlayerIds.Contains(p.PlayerId)).ToListAsync();
        double averageSkill = players.Any() ? players.Average(p => p.SkillLevel) : 0.0;

        // 3. Create the Match in Voting status
        var newMatch = new Match
        {
            MatchType = request.LobbyType,
            MatchSkillLevel = (int)Math.Round(averageSkill),
            Status = "Voting",
            PreferredTimeStart = timeStart,
            PreferredTimeEnd = timeEnd,
            SharedVenues = string.Join(",", request.SharedVenues)
        };

        await _db.Matches.AddAsync(newMatch);
        await _db.SaveChangesAsync();

        // 4. Create MatchParticipants
        foreach (var playerId in request.PlayerIds)
        {
            var participant = new MatchParticipant
            {
                MatchId = newMatch.MatchId,
                PlayerId = playerId
            };
            await _db.MatchParticipants.AddAsync(participant);
        }

        await _db.SaveChangesAsync();

        return Ok(new { MatchId = newMatch.MatchId });
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
                .GroupBy(mp => mp.VotedVenueId.Value)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key) // tie breaker: lower ID
                .Select(g => g.Key)
                .First();

            // Consensus: Tally time slot votes
            var timeWinner = match.MatchParticipants
                .GroupBy(mp => mp.VotedStartTime.Value)
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
                    Status = "Approved"
                };
                await _db.Bookings.AddAsync(booking);
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
            .Where(v => venueIds.Contains(v.VenueId))
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
}

