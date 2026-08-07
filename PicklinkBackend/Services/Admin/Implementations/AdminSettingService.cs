using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminSettingService : IAdminSettingService
{
    private static readonly Dictionary<string, PlatformSettingDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bookingHoldMinutes"] = new("Booking", "5", "Thời gian giữ chỗ khi chờ thanh toán", 1, 60),
        ["listingExpiryReminderDays"] = new("Phí lên sân", "7", "Số ngày trước hạn cần cảnh báo owner", 1, 30),
        ["maxReceiptUploadMb"] = new("Upload", "5", "Dung lượng tối đa cho biên lai thanh toán", 1, 10),
        ["highPriorityReportMinutes"] = new("Kiểm duyệt", "30", "SLA xử lý báo cáo ưu tiên cao", 5, 240)
    };

    private readonly IAdminRepository _adminRepository;

    public AdminSettingService(IAdminRepository adminRepository)
    {
        _adminRepository = adminRepository;
    }

    public async Task<List<AdminSettingResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var saved = await _adminRepository.GetPlatformSettingsAsync(cancellationToken);

        return Definitions.Select(definition =>
        {
            saved.TryGetValue(definition.Key, out var setting);
            return Map(definition.Key, definition.Value, setting);
        }).ToList();
    }

    public async Task<AdminSettingUpdateResult> UpdateAsync(
        string settingKey,
        AdminSettingUpdateRequest request,
        int? actorUserId,
        CancellationToken cancellationToken)
    {
        if (actorUserId is null) return AdminSettingUpdateResult.Unauthorized();

        if (!Definitions.TryGetValue(settingKey, out var definition))
            return AdminSettingUpdateResult.NotFound("Không tìm thấy cấu hình.");

        var value = request.SettingValue?.Trim();
        if (!int.TryParse(value, out var numericValue)
            || numericValue < definition.MinValue
            || numericValue > definition.MaxValue)
        {
            return AdminSettingUpdateResult.BadRequest(
                $"Giá trị phải từ {definition.MinValue} đến {definition.MaxValue}.");
        }

        var normalizedKey = Definitions.Keys.First(key => key.Equals(settingKey, StringComparison.OrdinalIgnoreCase));
        var setting = await _adminRepository.GetPlatformSettingByKeyAsync(normalizedKey, cancellationToken);
        if (setting is null)
        {
            setting = new PlatformSetting
            {
                SettingKey = normalizedKey,
                SettingGroup = definition.Group,
                Description = definition.Description
            };
            await _adminRepository.AddPlatformSettingAsync(setting, cancellationToken);
        }

        setting.SettingValue = numericValue.ToString();
        setting.SettingGroup = definition.Group;
        setting.Description = definition.Description;
        setting.UpdatedAt = DateTime.UtcNow;
        setting.UpdatedByUserId = actorUserId;

        await _adminRepository.SaveChangesAsync(cancellationToken);
        return AdminSettingUpdateResult.Success(Map(normalizedKey, definition, setting));
    }

    private static AdminSettingResponse Map(
        string key,
        PlatformSettingDefinition definition,
        PlatformSetting? setting) => new()
        {
            SettingKey = key,
            SettingValue = setting?.SettingValue ?? definition.DefaultValue,
            SettingGroup = definition.Group,
            Description = definition.Description,
            MinValue = definition.MinValue,
            MaxValue = definition.MaxValue,
            UpdatedAt = setting?.UpdatedAt
        };
}
