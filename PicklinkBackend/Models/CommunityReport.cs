namespace PicklinkBackend.Models;

public partial class CommunityReport
{
    public int CommunityReportId { get; set; }

    public int ReporterUserId { get; set; }

    public string TargetType { get; set; } = string.Empty;

    public int? TargetId { get; set; }

    public string TargetLabel { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Status { get; set; } = "Open";

    public string Priority { get; set; } = "Normal";

    public DateTime CreatedAt { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public int? ReviewedByUserId { get; set; }

    public string? ResolutionNote { get; set; }

    public virtual User ReporterUser { get; set; } = null!;

    public virtual User? ReviewedByUser { get; set; }
}
