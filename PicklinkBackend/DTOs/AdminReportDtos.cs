using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public sealed class AdminReportReviewRequest
{
    [Required]
    public string Status { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? ResolutionNote { get; set; }
}

public sealed class AdminReportResponse
{
    public int CommunityReportId { get; set; }
    public int ReporterUserId { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public string ReporterEmail { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public int? TargetId { get; set; }
    public string TargetLabel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByName { get; set; }
    public string? ResolutionNote { get; set; }
}