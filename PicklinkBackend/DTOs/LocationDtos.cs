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

public sealed record GeocodeCoordinatesResponse(double Latitude, double Longitude);

public sealed record ReverseGeocodeResponse(
    string DisplayName,
    string Province,
    string Ward);

public sealed record GeocodingSearchResultResponse(
    long PlaceId,
    string DisplayName,
    double Latitude,
    double Longitude);
