using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Locations;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/locations")]
public class LocationsController : ControllerBase
{
    private readonly LocationQueryService _locations;
    private readonly GeocodingService _geocoding;

    public LocationsController(LocationQueryService locations, GeocodingService geocoding)
    {
        _locations = locations;
        _geocoding = geocoding;
    }

    [HttpGet("provinces")]
    [ProducesResponseType(typeof(List<ProvinceResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ProvinceResponse>>> GetProvinces(CancellationToken cancellationToken)
    {
        return Ok(await _locations.ListProvincesAsync(cancellationToken));
    }

    [HttpGet("provinces/{provinceCode}/wards")]
    [ProducesResponseType(typeof(List<WardResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<WardResponse>>> GetWards(
        string provinceCode,
        CancellationToken cancellationToken)
    {
        return Ok(await _locations.ListWardsAsync(provinceCode, cancellationToken));
    }

    [HttpGet("geocode/forward")]
    [ProducesResponseType(typeof(GeocodeCoordinatesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<GeocodeCoordinatesResponse?>> ForwardGeocode(
        [FromQuery] string? province,
        [FromQuery] string? ward,
        CancellationToken cancellationToken)
    {
        province = province?.Trim();
        ward = string.IsNullOrWhiteSpace(ward) ? null : ward.Trim();
        if (string.IsNullOrWhiteSpace(province) || province.Length > 100 || ward?.Length > 100)
        {
            return BadRequest(new { message = "Province is required and area names must not exceed 100 characters." });
        }

        try
        {
            return Ok(await _geocoding.ForwardAsync(province, ward, cancellationToken));
        }
        catch (GeocodingServiceException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = exception.Message });
        }
    }

    [HttpGet("geocode/reverse")]
    [ProducesResponseType(typeof(ReverseGeocodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ReverseGeocodeResponse>> ReverseGeocode(
        [FromQuery] double latitude,
        [FromQuery] double longitude,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(latitude)
            || !double.IsFinite(longitude)
            || latitude is < -90 or > 90
            || longitude is < -180 or > 180)
        {
            return BadRequest(new { message = "Coordinates are invalid." });
        }

        try
        {
            return Ok(await _geocoding.ReverseAsync(latitude, longitude, cancellationToken));
        }
        catch (GeocodingServiceException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = exception.Message });
        }
    }

    [HttpGet("geocode/search")]
    [ProducesResponseType(typeof(List<GeocodingSearchResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<List<GeocodingSearchResultResponse>>> SearchAddresses(
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        query = query?.Trim();
        if (query is null || query.Length is < 3 or > 200)
        {
            return BadRequest(new { message = "Query must contain between 3 and 200 characters." });
        }

        try
        {
            return Ok(await _geocoding.SearchAsync(query, cancellationToken));
        }
        catch (GeocodingServiceException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = exception.Message });
        }
    }
}
