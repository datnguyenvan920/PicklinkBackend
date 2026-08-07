using PicklinkBackend.DTOs;
using PicklinkBackend.Repositories;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminBookingService : IAdminBookingService
{
    private readonly IAdminRepository _adminRepository;

    public AdminBookingService(IAdminRepository adminRepository)
    {
        _adminRepository = adminRepository;
    }

    public async Task<PaginatedResponse<AdminBookingSummaryResponse>> ListAsync(
        string? search,
        string? status,
        string? paymentStatus,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();
        var normalizedStatus = Normalize(status);
        var normalizedPaymentStatus = Normalize(paymentStatus);

        var (items, totalCount) = await _adminRepository.GetAdminBookingListAsync(
            keyword,
            normalizedStatus,
            normalizedPaymentStatus,
            page,
            pageSize,
            cancellationToken);

        return Pagination.Create(items, totalCount, page, pageSize);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}
