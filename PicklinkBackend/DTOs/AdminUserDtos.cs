namespace PicklinkBackend.DTOs;

public class AdminUserLockRequest
{
    public string? Reason { get; set; }
}

public class AdminUserSummaryResponse
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string RoleLabel { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public string? City { get; set; }
    public string? Commune { get; set; }
    public string? AvatarUrl { get; set; }
    public int JoinedClubCount { get; set; }
    public int OwnedVenueCount { get; set; }
    public int BookingCount { get; set; }
}
