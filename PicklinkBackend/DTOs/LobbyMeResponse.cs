namespace PicklinkBackend.DTOs;

/// <summary>
/// Returned by GET /api/match/lobby-me.
/// Contains everything the Flutter home-screen needs to render the
/// current user's lobby slot.
/// </summary>
public class LobbyMeResponse
{
    public int UserId { get; set; }

    /// <summary>The Player table primary key. Null when the user has no Player profile yet.</summary>
    public int? PlayerId { get; set; }

    /// <summary>Display name shown in the lobby slot.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Two-letter initials derived from the username,
    /// used as the avatar fallback when no profile image is available.
    /// </summary>
    public string AvatarInitials { get; set; } = string.Empty;

    /// <summary>
    /// Raw numeric skill level from the Player profile row.
    /// 0 when the user has no Player profile yet.
    /// </summary>
    public double SkillLevel { get; set; }

    /// <summary>
    /// Human-readable tier derived from SkillLevel:
    /// Bronze | Silver | Gold | Platinum | Diamond
    /// </summary>
    public string Tier { get; set; } = "Bronze";

    /// <summary>Prestige points. 0 when no Player profile exists.</summary>
    public int Prestige { get; set; }

    public string? ProfileImageUrl { get; set; }

    // ── Tier mapping ──────────────────────────────────────────────────────────

    public static string TierFromSkillLevel(double skill) => skill switch
    {
        < 1 => "Bronze",
        < 2 => "Silver",
        < 3 => "Gold",
        < 4 => "Platinum",
        _      => "Diamond",
    };

    /// <summary>
    /// Derives two uppercase initials from a username, e.g. "minh_tu" → "MT".
    /// Falls back to the first two characters if no word boundary is found.
    /// </summary>
    public static string InitialsFromUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return "??";

        // Split on spaces, underscores or dots
        var parts = username.Trim().Split(new[] { ' ', '_', '.' },
            System.StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
            return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}";

        // Single-word username: take first two chars
        var name = username.Trim();
        return name.Length >= 2
            ? $"{char.ToUpper(name[0])}{char.ToUpper(name[1])}"
            : char.ToUpper(name[0]).ToString();
    }
}
