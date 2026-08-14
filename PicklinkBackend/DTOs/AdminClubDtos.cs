using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public sealed class AdminClubModerationRequest
{
    public bool IsSuspended { get; set; }

    [StringLength(1000)]
    public string? SuspensionReason { get; set; }
}

public class AdminClubResponse
{
    public int GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string GroupType { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public int PostCount { get; set; }
    public bool IsSuspended { get; set; }
    public string? SuspensionReason { get; set; }
    public DateTime? ModeratedAt { get; set; }
    public string? ModeratedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}
