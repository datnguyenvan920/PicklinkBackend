using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Controllers;

public partial class AuthController
{
    /// <summary>
    /// Checks whether the currently authenticated user has been assigned a role.
    /// A user is considered to have no role yet when their UserType is still "User"
    /// (the default value set at registration). Any other value is treated as a real role.
    /// </summary>
    [Authorize]
    [HttpGet("role-status")]
    public async Task<ActionResult> RoleStatus()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        var hasRole = !string.Equals(user.UserType, "User", StringComparison.OrdinalIgnoreCase);

        return Ok(new
        {
            hasRole,
            userType = user.UserType
        });
    }

    /// <summary>
    /// Assigns a role to the currently authenticated user.
    /// Can only be called once - users whose UserType is already set to something
    /// other than "User" will receive a 409 Conflict.
    ///
    /// Supported roles:
    ///   - "VenueOwner" : Updates UserType only.
    ///   - "Player"     : Updates UserType and creates a Player profile row.
    ///                    Requires the Experience field (Beginner / Intermediate / Advanced).
    ///   - "Staff"      : Updates UserType only (Staff profile creation is not yet implemented).
    /// </summary>
    [Authorize]
    [HttpPost("assign-role")]
    public async Task<ActionResult> AssignRole(AssignRoleRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        // Guard: role has already been assigned
        if (!string.Equals(user.UserType, "User", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { message = $"Role is already assigned as '{user.UserType}'." });
        }

        var role = request.Role.Trim();

        switch (role)
        {
            case "VenueOwner":
                user.UserType = "VenueOwner";
                break;

            case "Player":
                if (request.Experience is null)
                {
                    return BadRequest(new { message = "Experience is required when assigning the Player role." });
                }

                user.UserType = "Player";

                var player = new Player
                {
                    UserId = user.UserId,
                    SkillLevel = MapExperienceToSkillLevel(request.Experience.Value),
                    Prestige = 100
                };

                _dbContext.Players.Add(player);
                break;

            case "Staff":
                // Staff profile creation is not yet implemented.
                user.UserType = "Staff";
                break;

            default:
                return BadRequest(new { message = $"Unknown role '{role}'. Accepted values: Player, VenueOwner, Staff." });
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = $"Role '{role}' assigned successfully.",
            userType = user.UserType
        });
    }

    /// <summary>Maps the self-reported experience level to a numeric skill level.</summary>
    private static double MapExperienceToSkillLevel(ExperienceLevel experience) => experience switch
    {
        ExperienceLevel.Beginner     => 1.0,
        ExperienceLevel.Intermediate => 1.5,
        ExperienceLevel.Advanced     => 2.0,
        _                            => 1.0
    };
}
