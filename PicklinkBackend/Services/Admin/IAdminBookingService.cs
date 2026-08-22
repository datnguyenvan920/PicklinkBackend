using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Admin;

public interface IAdminBookingService
{
    Task<PaginatedResponse<AdminBookingSummaryResponse>> ListAsync(
        string? search,
        string? status,
        string? paymentStatus,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<AdminBookingCancelResult> CancelAsync(
        int bookingId,
        string reason,
        int actorUserId,
        CancellationToken cancellationToken);

    Task<AdminBookingCancelResult> ResolveRefundDisputeAsync(
        int bookingId,
        string resolution,
        int actorUserId,
        CancellationToken cancellationToken);
}
