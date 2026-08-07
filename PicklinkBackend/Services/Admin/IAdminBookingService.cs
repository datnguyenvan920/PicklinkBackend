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
}
