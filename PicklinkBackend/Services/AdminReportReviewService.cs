using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services;

public sealed class AdminReportReviewService
{
    private static readonly string[] ReviewStatuses = ["Open", "InReview", "Resolved", "Dismissed"];
    private readonly ApplicationDbContext _dbContext;
    private readonly NotificationService _notifications;

    public AdminReportReviewService(
        ApplicationDbContext dbContext,
        NotificationService notifications)
    {
        _dbContext = dbContext;
        _notifications = notifications;
    }

    public async Task<AdminReportReviewResult> ReviewAsync(
        int reportId,
        AdminReportReviewRequest request,
        int? reviewerId,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = ReviewStatuses.FirstOrDefault(status =>
            status.Equals(request.Status?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (normalizedStatus is null || normalizedStatus == "Open")
        {
            return AdminReportReviewResult.BadRequest("Trạng thái xử lý báo cáo không hợp lệ.");
        }

        if (reviewerId is null) return AdminReportReviewResult.Unauthorized();

        var report = await _dbContext.CommunityReports
            .Include(item => item.ReporterUser)
            .Include(item => item.ReviewedByUser)
            .SingleOrDefaultAsync(item => item.CommunityReportId == reportId, cancellationToken);
        if (report is null) return AdminReportReviewResult.NotFound("Không tìm thấy báo cáo.");

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

        return AdminReportReviewResult.Success(AdminReportQueryService.Map(report));
    }
}

public sealed record AdminReportReviewResult(
    AdminReportReviewResultStatus Status,
    AdminReportResponse? Report,
    string? ErrorMessage)
{
    public static AdminReportReviewResult Success(AdminReportResponse report) =>
        new(AdminReportReviewResultStatus.Success, report, ErrorMessage: null);

    public static AdminReportReviewResult BadRequest(string errorMessage) =>
        new(AdminReportReviewResultStatus.BadRequest, Report: null, errorMessage);

    public static AdminReportReviewResult Unauthorized() =>
        new(AdminReportReviewResultStatus.Unauthorized, Report: null, ErrorMessage: null);

    public static AdminReportReviewResult NotFound(string errorMessage) =>
        new(AdminReportReviewResultStatus.NotFound, Report: null, errorMessage);
}

public enum AdminReportReviewResultStatus
{
    Success,
    BadRequest,
    Unauthorized,
    NotFound
}