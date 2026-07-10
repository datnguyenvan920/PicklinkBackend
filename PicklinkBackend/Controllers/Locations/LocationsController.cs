using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Locations;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/locations")]
public class LocationsController : ControllerBase
{
    private readonly LocationQueryService _locations;

    public LocationsController(LocationQueryService locations)
    {
        _locations = locations;
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
}
