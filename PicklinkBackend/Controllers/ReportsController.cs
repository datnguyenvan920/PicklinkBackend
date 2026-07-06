using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Data;
using PicklinkBackend.Models;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private static readonly HashSet<string> TargetTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "User",
        "Venue",
        "Booking",
        "Post",
        "Club",
        "Payment",
        "Other"
    };

    private readonly ApplicationDbContext _dbContext;

    public ReportsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpPost]
    public async Task<ActionResult<ReportSubmissionResponse>> CreateReport(
        ReportSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var reporterUserId = CurrentUserId();
        if (reporterUserId is null) return Unauthorized();

        var targetType = request.TargetType.Trim();
        if (!TargetTypes.Contains(targetType))
            return BadRequest(new { message = "Loại báo cáo không hợp lệ." });

        var report = new CommunityReport
        {
            ReporterUserId = reporterUserId.Value,
            TargetType = TargetTypes.First(type => type.Equals(targetType, StringComparison.OrdinalIgnoreCase)),
            TargetId = request.TargetId,
            TargetLabel = request.TargetLabel.Trim(),
            Reason = request.Reason.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Status = "Open",
            Priority = "Normal",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.CommunityReports.Add(report);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Created($"/api/reports/{report.CommunityReportId}", new ReportSubmissionResponse
        {
            CommunityReportId = report.CommunityReportId,
            Status = report.Status,
            CreatedAt = report.CreatedAt
        });
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
}

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
