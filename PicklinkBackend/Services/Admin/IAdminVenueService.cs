using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Admin;

public interface IAdminVenueService
{
    Task<AdminVenueListResult> ListAsync(
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<AdminVenueDetailResponse?> GetDetailAsync(
        int venueId,
        CancellationToken cancellationToken);

    Task<AdminVenueApprovalResult> ApproveAsync(
        int venueId,
        int actorUserId,
        CancellationToken cancellationToken);

    Task<AdminVenueApprovalResult> RejectAsync(
        int venueId,
        string? reason,
        int actorUserId,
        CancellationToken cancellationToken);

    Task<PaginatedResponse<AdminBookingSummaryResponse>> ListBookingsAsync(
        string? search,
        string? status,
        string? paymentStatus,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
