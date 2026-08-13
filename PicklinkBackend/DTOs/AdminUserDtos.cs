namespace PicklinkBackend.DTOs;

public sealed class AdminUserLockRequest
{
    public string? Reason { get; set; }
}

public class AdminUserResponse
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string RoleLabel { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public string? LockReason { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? LockedByName { get; set; }
    public DateTime? UnlockedAt { get; set; }
    public string? UnlockedByName { get; set; }
    public string? City { get; set; }
    public string? Commune { get; set; }
    public string? AvatarUrl { get; set; }
    public int JoinedClubCount { get; set; }
    public int OwnedVenueCount { get; set; }
    public int BookingCount { get; set; }
}

public class AdminUserSummaryResponse : AdminUserResponse {}
