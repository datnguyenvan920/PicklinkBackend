using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Admin;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Notifications.Implementations;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminReportService : IAdminReportService
{
    private static readonly string[] ReviewStatuses = ["Open", "InReview", "Resolved", "Dismissed"];
    private readonly IAdminRepository _adminRepository;
    private readonly NotificationService _notifications;

    public AdminReportService(
        IAdminRepository adminRepository,
        NotificationService notifications)
    {
        _adminRepository = adminRepository;
        _notifications = notifications;
    }

    public async Task<PaginatedResponse<AdminReportResponse>> ListAsync(
        string? search,
        string? status,
        string? targetType,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();
        var normalizedStatus = Normalize(status);
        var normalizedTargetType = Normalize(targetType);

        var (items, totalCount) = await _adminRepository.GetAdminReportListAsync(
            keyword, normalizedStatus, normalizedTargetType, page, pageSize, cancellationToken);

        return Pagination.Create(items, totalCount, page, pageSize);
    }

    public async Task<AdminReportReviewResult> ReviewAsync(
        int reportId,
        AdminReportReviewRequest request,
        int? reviewerId,
        string? reviewerName,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = ReviewStatuses.FirstOrDefault(status =>
            status.Equals(request.Status?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (normalizedStatus is null || normalizedStatus == "Open")
        {
            return AdminReportReviewResult.BadRequest("Trạng thái xử lý báo cáo không hợp lệ.");
        }

        if (reviewerId is null) return AdminReportReviewResult.Unauthorized();

        var report = await _adminRepository.GetCommunityReportByIdAsync(reportId, cancellationToken);
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

        await _adminRepository.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();

        var response = Map(report);
        response.ReviewedByName = reviewerName;
        return AdminReportReviewResult.Success(response);
    }

    public static AdminReportResponse Map(CommunityReport report) => new()
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
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}
