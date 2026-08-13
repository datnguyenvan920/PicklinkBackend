using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Admin;

public interface IAdminClubService
{
    Task<PaginatedResponse<AdminClubResponse>> ListAsync(
        string? search,
        bool? suspendedOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<AdminClubModerationResult> ModerateAsync(
        int groupId,
        AdminClubModerationRequest request,
        int moderatorId,
        string? moderatorName,
        CancellationToken cancellationToken);
}
