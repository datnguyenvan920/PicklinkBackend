using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PicklinkBackend.DTOs;

/// <summary>
/// The experience levels a player can self-report, which are mapped to a numeric skill level.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExperienceLevel
{
    /// <summary>Beginner â€” SkillLevel 1.0</summary>
    Beginner,

    /// <summary>Intermediate â€” SkillLevel 1.5</summary>
    Intermediate,

    /// <summary>Advanced â€” SkillLevel 2.0</summary>
    Advanced
}

/// <summary>
/// Request body for POST /api/auth/assign-role.
/// </summary>
public class AssignRoleRequest
{
    /// <summary>
    /// The role to assign. Accepted values: "Player", "VenueOwner", "Staff".
    /// </summary>
    [Required]
    public string Role { get; set; } = string.Empty;

    // â”€â”€ Player-specific fields â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Required when Role is "Player". The player's self-reported experience level,
    /// which is translated to a numeric SkillLevel (Beginner=1, Intermediate=1.5, Advanced=2).
    /// </summary>
    public ExperienceLevel? Experience { get; set; }
}
