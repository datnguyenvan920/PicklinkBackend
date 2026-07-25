using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Admin;

public interface IAdminDashboardService
{
    Task<AdminDashboardResponse> GetDashboardAsync(CancellationToken cancellationToken);
}
