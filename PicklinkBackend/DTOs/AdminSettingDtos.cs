using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

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
