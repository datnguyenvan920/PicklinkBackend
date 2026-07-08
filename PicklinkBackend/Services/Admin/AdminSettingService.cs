using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Admin;

public sealed class AdminSettingService
{
    private static readonly Dictionary<string, PlatformSettingDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bookingHoldMinutes"] = new("Booking", "5", "ThÃ¡Â»Âi gian giÃ¡Â»Â¯ chÃ¡Â»â€” khi chÃ¡Â»Â thanh toÃƒÂ¡n", 1, 60),
        ["listingExpiryReminderDays"] = new("PhÃƒÂ­ lÃƒÂªn sÃƒÂ¢n", "7", "SÃ¡Â»â€˜ ngÃƒÂ y trÃ†Â°Ã¡Â»â€ºc hÃ¡ÂºÂ¡n cÃ¡ÂºÂ§n cÃ¡ÂºÂ£nh bÃƒÂ¡o owner", 1, 30),
        ["maxReceiptUploadMb"] = new("Upload", "5", "Dung lÃ†Â°Ã¡Â»Â£ng tÃ¡Â»â€˜i Ã„â€˜a cho biÃƒÂªn lai thanh toÃƒÂ¡n", 1, 10),
        ["highPriorityReportMinutes"] = new("KiÃ¡Â»Æ’m duyÃ¡Â»â€¡t", "30", "SLA xÃ¡Â»Â­ lÃƒÂ½ bÃƒÂ¡o cÃƒÂ¡o Ã†Â°u tiÃƒÂªn cao", 5, 240)
    };

    private readonly ApplicationDbContext _dbContext;

    public AdminSettingService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<AdminSettingResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var saved = await _dbContext.PlatformSettings.AsNoTracking()
            .ToDictionaryAsync(setting => setting.SettingKey, StringComparer.OrdinalIgnoreCase, cancellationToken);

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
        if (!Definitions.TryGetValue(settingKey, out var definition))
            return AdminSettingUpdateResult.NotFound("KhÃƒÂ´ng tÃƒÂ¬m thÃ¡ÂºÂ¥y cÃ¡ÂºÂ¥u hÃƒÂ¬nh.");

        var value = request.SettingValue?.Trim();
        if (!int.TryParse(value, out var numericValue)
            || numericValue < definition.MinValue
            || numericValue > definition.MaxValue)
        {
            return AdminSettingUpdateResult.BadRequest(
                $"GiÃƒÂ¡ trÃ¡Â»â€¹ phÃ¡ÂºÂ£i tÃ¡Â»Â« {definition.MinValue} Ã„â€˜Ã¡ÂºÂ¿n {definition.MaxValue}.");
        }

        var normalizedKey = Definitions.Keys.First(key => key.Equals(settingKey, StringComparison.OrdinalIgnoreCase));
        var setting = await _dbContext.PlatformSettings
            .SingleOrDefaultAsync(item => item.SettingKey == normalizedKey, cancellationToken);
        if (setting is null)
        {
            setting = new PlatformSetting
            {
                SettingKey = normalizedKey,
                SettingGroup = definition.Group,
                Description = definition.Description
            };
            _dbContext.PlatformSettings.Add(setting);
        }

        setting.SettingValue = numericValue.ToString();
        setting.SettingGroup = definition.Group;
        setting.Description = definition.Description;
        setting.UpdatedAt = DateTime.UtcNow;
        setting.UpdatedByUserId = actorUserId;

        await _dbContext.SaveChangesAsync(cancellationToken);
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

public sealed record PlatformSettingDefinition(
    string Group,
    string DefaultValue,
    string Description,
    int MinValue,
    int MaxValue);

public sealed record AdminSettingUpdateResult(
    AdminSettingUpdateResultStatus Status,
    AdminSettingResponse? Setting = null,
    string? ErrorMessage = null)
{
    public static AdminSettingUpdateResult Success(AdminSettingResponse setting) =>
        new(AdminSettingUpdateResultStatus.Success, setting, ErrorMessage: null);

    public static AdminSettingUpdateResult BadRequest(string errorMessage) =>
        new(AdminSettingUpdateResultStatus.BadRequest, Setting: null, ErrorMessage: errorMessage);

    public static AdminSettingUpdateResult NotFound(string errorMessage) =>
        new(AdminSettingUpdateResultStatus.NotFound, Setting: null, ErrorMessage: errorMessage);
}

public enum AdminSettingUpdateResultStatus
{
    Success,
    BadRequest,
    NotFound
}
