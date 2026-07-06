using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/reports")]
public class AdminReportsController : ControllerBase
{
    private static readonly string[] ReviewStatuses = ["Open", "InReview", "Resolved", "Dismissed"];
    private readonly ApplicationDbContext _dbContext;
    private readonly NotificationService _notifications;

    public AdminReportsController(
        ApplicationDbContext dbContext,
        NotificationService notifications)
    {
        _dbContext = dbContext;
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminReportResponse>>> GetReports(
        string? search,
        string? status,
        string? targetType,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();
        var normalizedStatus = Normalize(status);
        var normalizedTargetType = Normalize(targetType);

        var query = _dbContext.CommunityReports.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(report =>
                report.TargetLabel.Contains(keyword)
                || report.Reason.Contains(keyword)
                || (report.Description != null && report.Description.Contains(keyword))
                || report.ReporterUser.Username.Contains(keyword)
                || report.ReporterUser.Email.Contains(keyword));
        }
        if (normalizedStatus is not null)
            query = query.Where(report => report.Status == normalizedStatus);
        if (normalizedTargetType is not null)
            query = query.Where(report => report.TargetType == normalizedTargetType);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(report => report.Status == "Open")
            .ThenByDescending(report => report.Priority == "High")
            .ThenByDescending(report => report.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(report => new AdminReportResponse
            {
                CommunityReportId = report.CommunityReportId,
                ReporterUserId = report.ReporterUserId,
                ReporterName = report.ReporterUser.Username,
                ReporterEmail = report.ReporterUser.Email,
                TargetType = report.TargetType,
                TargetId = report.TargetId,
                TargetLabel = report.TargetLabel,
                Reason = report.Reason,
                Description = report.Description,
                Status = report.Status,
                Priority = report.Priority,
                CreatedAt = report.CreatedAt,
                ReviewedAt = report.ReviewedAt,
                ReviewedByName = report.ReviewedByUser != null ? report.ReviewedByUser.Username : null,
                ResolutionNote = report.ResolutionNote
            })
            .ToListAsync(cancellationToken);

        return Ok(Pagination.Create(items, totalCount, page, pageSize));
    }

    [HttpPost("{reportId:int}/review")]
    public async Task<ActionResult<AdminReportResponse>> ReviewReport(
        int reportId,
        AdminReportReviewRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = ReviewStatuses.FirstOrDefault(status =>
            status.Equals(request.Status?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (normalizedStatus is null || normalizedStatus == "Open")
            return BadRequest(new { message = "Trạng thái xử lý báo cáo không hợp lệ." });

        var report = await _dbContext.CommunityReports
            .Include(item => item.ReporterUser)
            .Include(item => item.ReviewedByUser)
            .SingleOrDefaultAsync(item => item.CommunityReportId == reportId, cancellationToken);
        if (report is null) return NotFound(new { message = "Không tìm thấy báo cáo." });

        var reviewerId = CurrentUserId();
        if (reviewerId is null) return Unauthorized();

        report.Status = normalizedStatus;
        report.ReviewedAt = DateTime.UtcNow;
        report.ReviewedByUserId = reviewerId.Value;
        report.ResolutionNote = string.IsNullOrWhiteSpace(request.ResolutionNote)
            ? null
            : request.ResolutionNote.Trim();

        _notifications.Add(new NotificationInput(
            UserId: report.ReporterUserId,
            Type: NotificationTypes.System,
            Title: "Báo cáo đã được xử lý",
            Message: $"Báo cáo về \"{report.TargetLabel}\" đã được cập nhật trạng thái {report.Status}.",
            Tone: report.Status == "Resolved" ? NotificationTones.Success : NotificationTones.Default,
            LinkTo: "/notifications",
            LinkLabel: "Xem thông báo"));

        await _dbContext.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();

        return Ok(new AdminReportResponse
        {
            CommunityReportId = report.CommunityReportId,
            ReporterUserId = report.ReporterUserId,
            ReporterName = report.ReporterUser.Username,
            ReporterEmail = report.ReporterUser.Email,
            TargetType = report.TargetType,
            TargetId = report.TargetId,
            TargetLabel = report.TargetLabel,
            Reason = report.Reason,
            Description = report.Description,
            Status = report.Status,
            Priority = report.Priority,
            CreatedAt = report.CreatedAt,
            ReviewedAt = report.ReviewedAt,
            ReviewedByName = report.ReviewedByUser?.Username,
            ResolutionNote = report.ResolutionNote
        });
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}

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
