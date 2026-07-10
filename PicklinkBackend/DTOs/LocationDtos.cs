namespace PicklinkBackend.DTOs;

public sealed class ProvinceResponse
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}

public sealed class WardResponse
{
    public string Code { get; set; } = string.Empty;
    public string ProvinceCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}