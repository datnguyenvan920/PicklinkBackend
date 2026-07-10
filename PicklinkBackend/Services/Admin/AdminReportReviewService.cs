using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Notifications;

namespace PicklinkBackend.Services.Admin;

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
            return AdminReportReviewResult.BadRequest("TrÃƒÂ¡Ã‚ÂºÃ‚Â¡ng thÃƒÆ’Ã‚Â¡i xÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÆ’Ã‚Â½ bÃƒÆ’Ã‚Â¡o cÃƒÆ’Ã‚Â¡o khÃƒÆ’Ã‚Â´ng hÃƒÂ¡Ã‚Â»Ã‚Â£p lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡.");
        }

        if (reviewerId is null) return AdminReportReviewResult.Unauthorized();

        var report = await _dbContext.CommunityReports
            .Include(item => item.ReporterUser)
            .Include(item => item.ReviewedByUser)
            .SingleOrDefaultAsync(item => item.CommunityReportId == reportId, cancellationToken);
        if (report is null) return AdminReportReviewResult.NotFound("KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y bÃƒÆ’Ã‚Â¡o cÃƒÆ’Ã‚Â¡o.");

        report.Status = normalizedStatus;
        report.ReviewedAt = DateTime.UtcNow;
        report.ReviewedByUserId = reviewerId.Value;
        report.ResolutionNote = string.IsNullOrWhiteSpace(request.ResolutionNote)
            ? null
            : request.ResolutionNote.Trim();

        _notifications.Add(new NotificationInput(
            UserId: report.ReporterUserId,
            Type: NotificationTypes.System,
            Title: "BÃƒÆ’Ã‚Â¡o cÃƒÆ’Ã‚Â¡o Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÆ’Ã‚Â½",
            Message: $"BÃƒÆ’Ã‚Â¡o cÃƒÆ’Ã‚Â¡o vÃƒÂ¡Ã‚Â»Ã‚Â \"{report.TargetLabel}\" Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c cÃƒÂ¡Ã‚ÂºÃ‚Â­p nhÃƒÂ¡Ã‚ÂºÃ‚Â­t trÃƒÂ¡Ã‚ÂºÃ‚Â¡ng thÃƒÆ’Ã‚Â¡i {report.Status}.",
            Tone: report.Status == "Resolved" ? NotificationTones.Success : NotificationTones.Default,
            LinkTo: "/notifications",
            LinkLabel: "Xem thÃƒÆ’Ã‚Â´ng bÃƒÆ’Ã‚Â¡o"));

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