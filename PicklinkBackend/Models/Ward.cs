namespace PicklinkBackend.Models;

public partial class Ward
{
    public string Code { get; set; } = string.Empty;

    public string ProvinceCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public virtual Province Province { get; set; } = null!;
}