using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;

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
}

