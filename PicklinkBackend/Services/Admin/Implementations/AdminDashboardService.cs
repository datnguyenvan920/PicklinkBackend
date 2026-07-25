using PicklinkBackend.DTOs;
using PicklinkBackend.Repositories;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminDashboardService : IAdminDashboardService
{
    private readonly IAdminRepository _adminRepository;

    public AdminDashboardService(IAdminRepository adminRepository)
    {
        _adminRepository = adminRepository;
    }

    public Task<AdminDashboardResponse> GetDashboardAsync(CancellationToken cancellationToken) =>
        _adminRepository.GetAdminDashboardAsync(cancellationToken);
}
