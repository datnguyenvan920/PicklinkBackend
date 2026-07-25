using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Admin;

public interface IAdminSettingService
{
    Task<List<AdminSettingResponse>> ListAsync(CancellationToken cancellationToken);
    Task<AdminSettingUpdateResult> UpdateAsync(
        string settingKey,
        AdminSettingUpdateRequest request,
        int? actorUserId,
        CancellationToken cancellationToken);
}
