using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
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
            return BadRequest(new { message = "radiusKm must be between 0 and 50." });

        if (lat < -90 || lat > 90 || lng < -180 || lng > 180)
            return BadRequest(new { message = "Invalid coordinates." });

        return Ok(await _nearby.GetNearbyAsync(lat, lng, radiusKm, cancellationToken));
    }
}