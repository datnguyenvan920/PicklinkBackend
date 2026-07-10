using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public sealed class ReportSubmissionRequest
{
    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string TargetType { get; set; } = string.Empty;

    public int? TargetId { get; set; }

    [Required]
    [StringLength(250, MinimumLength = 2)]
    public string TargetLabel { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }
}

public sealed class ReportSubmissionResponse
{
    public int CommunityReportId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}