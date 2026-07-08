namespace PicklinkBackend.Models;

public partial class Province
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public virtual ICollection<Ward> Wards { get; set; } = new List<Ward>();
}