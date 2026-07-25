using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Admin;

public interface IAdminReviewService
{
    Task<PaginatedResponse<AdminReviewResponse>> ListAsync(
        string? search,
        string? moderationStatus,
        string? targetType,
        int? score,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    string? Validate(AdminReviewModerationRequest request);

    Task<AdminReviewModerationResult> ModerateAsync(
        int ratingId,
        AdminReviewModerationRequest request,
        int reviewerId,
        CancellationToken cancellationToken);
}
