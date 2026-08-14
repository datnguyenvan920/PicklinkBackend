using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Admin;

public interface IAdminReportService
{
    Task<PaginatedResponse<AdminReportResponse>> ListAsync(
        string? search,
        string? status,
        string? targetType,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<AdminReportReviewResult> ReviewAsync(
        int reportId,
        AdminReportReviewRequest request,
        int? reviewerId,
        string? reviewerName,
        CancellationToken cancellationToken);
}
