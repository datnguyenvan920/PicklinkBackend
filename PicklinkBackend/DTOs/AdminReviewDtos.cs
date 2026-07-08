using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public sealed class AdminReviewModerationRequest
{
    public bool IsHidden { get; set; }

    [Required]
    public string ModerationStatus { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? ModerationNote { get; set; }
}

public sealed class AdminReviewResponse
{
    public int RatingId { get; set; }
    public int ReviewerUserId { get; set; }
    public string ReviewerName { get; set; } = string.Empty;
    public string? ReviewerEmail { get; set; }
    public int? BookingId { get; set; }
    public int TargetId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public int Score { get; set; }
    public string? Comment { get; set; }
    public string? Tags { get; set; }
    public bool IsAnonymous { get; set; }
    public bool IsHidden { get; set; }
    public string ModerationStatus { get; set; } = string.Empty;
    public string? ModerationNote { get; set; }
    public DateTime? ModeratedAt { get; set; }
    public string? ModeratedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}
