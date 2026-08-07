using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;

namespace PicklinkBackend.Services.Community.Implementations;

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

    private readonly ICommunityRepository _communityRepository;

    public CommunityReportSubmissionService(ICommunityRepository communityRepository)
    {
        _communityRepository = communityRepository;
    }

    public async Task<ReportSubmissionResult> CreateAsync(
        ReportSubmissionRequest request,
        int? reporterUserId,
        CancellationToken cancellationToken)
    {
        if (reporterUserId is null) return ReportSubmissionResult.Unauthorized();

        var targetType = request.TargetType.Trim();
        if (!TargetTypes.Contains(targetType))
            return ReportSubmissionResult.BadRequest("Loại báo cáo không hợp lệ.");

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

        await _communityRepository.AddCommunityReportAsync(report, cancellationToken);
        await _communityRepository.SaveChangesAsync(cancellationToken);

        return ReportSubmissionResult.Success(new ReportSubmissionResponse
        {
            CommunityReportId = report.CommunityReportId,
            Status = report.Status,
            CreatedAt = report.CreatedAt
        });
    }
}
