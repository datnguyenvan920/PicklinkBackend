namespace PicklinkBackend.Models;

public partial class PlatformSetting
{
    public int PlatformSettingId { get; set; }

    public string SettingKey { get; set; } = string.Empty;

    public string SettingValue { get; set; } = string.Empty;

    public string SettingGroup { get; set; } = "General";

    public string Description { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }

    public int? UpdatedByUserId { get; set; }

    public virtual User? UpdatedByUser { get; set; }
}
