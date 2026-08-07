using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Venues;
using PicklinkBackend.Services.Venues.Implementations;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/venues")]
public class VenueController : ControllerBase
{
    private readonly VenueNearbyQueryService _nearby;

    public VenueController(VenueNearbyQueryService nearby)
    {
        _nearby = nearby;
    }

    [HttpGet("nearby")]
    [ProducesResponseType(typeof(List<VenueResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<VenueResponse>>> GetNearby(
        [FromQuery] double lat,
        [FromQuery] double lng,
        [FromQuery] double radiusKm = 5.0,
        CancellationToken cancellationToken = default)
    {
        if (radiusKm <= 0 || radiusKm > 50)
            return BadRequest(new { message = "Bán kính tìm kiếm phải từ 0 đến 50 km." });

        if (lat < -90 || lat > 90 || lng < -180 || lng > 180)
            return BadRequest(new { message = "Tọa độ không hợp lệ." });

        return Ok(await _nearby.GetNearbyAsync(lat, lng, radiusKm, cancellationToken));
    }
}