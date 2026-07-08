using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Community;

public sealed class CommunityReportSubmissionService
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

    public CommunityReportSubmissionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ReportSubmissionResult> CreateAsync(
        ReportSubmissionRequest request,
        int? reporterUserId,
        CancellationToken cancellationToken)
    {
        if (reporterUserId is null) return ReportSubmissionResult.Unauthorized();

        var targetType = request.TargetType.Trim();
        if (!TargetTypes.Contains(targetType))
            return ReportSubmissionResult.BadRequest("Loai bao cao khong hop le.");

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

        return ReportSubmissionResult.Success(new ReportSubmissionResponse
        {
            CommunityReportId = report.CommunityReportId,
            Status = report.Status,
            CreatedAt = report.CreatedAt
        });
    }
}

public sealed record ReportSubmissionResult(
    ReportSubmissionResultStatus Status,
    ReportSubmissionResponse? Report,
    string? ErrorMessage)
{
    public static ReportSubmissionResult Success(ReportSubmissionResponse report) =>
        new(ReportSubmissionResultStatus.Success, report, ErrorMessage: null);

    public static ReportSubmissionResult BadRequest(string errorMessage) =>
        new(ReportSubmissionResultStatus.BadRequest, Report: null, errorMessage);

    public static ReportSubmissionResult Unauthorized() =>
        new(ReportSubmissionResultStatus.Unauthorized, Report: null, ErrorMessage: null);
}

public enum ReportSubmissionResultStatus
{
    Success,
    BadRequest,
    Unauthorized
}