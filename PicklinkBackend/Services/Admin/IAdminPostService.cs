using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Admin;

public interface IAdminPostService
{
    Task<PaginatedResponse<AdminPostResponse>> ListAsync(
        string? search,
        bool? hiddenOnly,
        int? groupId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<AdminPostModerationResult> ModerateAsync(
        int postId,
        AdminPostModerationRequest request,
        int moderatorId,
        string? moderatorName,
        CancellationToken cancellationToken);

    Task<AdminPostDeleteResult> DeleteAsync(
        int postId,
        CancellationToken cancellationToken);
}
