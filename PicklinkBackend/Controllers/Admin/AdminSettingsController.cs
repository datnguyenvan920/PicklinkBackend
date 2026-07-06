using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.Models;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/settings")]
public class AdminSettingsController : ControllerBase
{
    private static readonly Dictionary<string, PlatformSettingDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bookingHoldMinutes"] = new("Booking", "5", "Thời gian giữ chỗ khi chờ thanh toán", 1, 60),
        ["listingExpiryReminderDays"] = new("Phí lên sàn", "7", "Số ngày trước hạn cần cảnh báo owner", 1, 30),
        ["maxReceiptUploadMb"] = new("Upload", "5", "Dung lượng tối đa cho biên lai thanh toán", 1, 10),
        ["highPriorityReportMinutes"] = new("Kiểm duyệt", "30", "SLA xử lý báo cáo ưu tiên cao", 5, 240)
    };

    private readonly ApplicationDbContext _dbContext;

    public AdminSettingsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdminSettingResponse>>> GetSettings(CancellationToken cancellationToken)
    {
        var saved = await _dbContext.PlatformSettings.AsNoTracking()
            .ToDictionaryAsync(setting => setting.SettingKey, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return Ok(Definitions.Select(definition =>
        {
            saved.TryGetValue(definition.Key, out var setting);
            return Map(definition.Key, definition.Value, setting);
        }).ToList());
    }

    [HttpPut("{settingKey}")]
    public async Task<ActionResult<AdminSettingResponse>> UpdateSetting(
        string settingKey,
        AdminSettingUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!Definitions.TryGetValue(settingKey, out var definition))
            return NotFound(new { message = "Không tìm thấy cấu hình." });

        var value = request.SettingValue?.Trim();
        if (!int.TryParse(value, out var numericValue)
            || numericValue < definition.MinValue
            || numericValue > definition.MaxValue)
        {
            return BadRequest(new { message = $"Giá trị phải từ {definition.MinValue} đến {definition.MaxValue}." });
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
        setting.UpdatedByUserId = CurrentUserId();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(Map(normalizedKey, definition, setting));
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;

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

public sealed class AdminSettingUpdateRequest
{
    [Required]
    public string SettingValue { get; set; } = string.Empty;
}

public sealed class AdminSettingResponse
{
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public string SettingGroup { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MinValue { get; set; }
    public int MaxValue { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed record PlatformSettingDefinition(
    string Group,
    string DefaultValue,
    string Description,
    int MinValue,
    int MaxValue);
