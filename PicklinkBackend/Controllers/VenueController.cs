using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VenueController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public VenueController(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns all venues from the database that fall within <paramref name="radiusKm"/>
    /// kilometres of the given (<paramref name="lat"/>, <paramref name="lng"/>) coordinates.
    ///
    /// No authentication required — the map can be browsed anonymously.
    ///
    /// Results are sorted by distance (nearest first) and capped at 50 venues.
    /// Only venues that have both Latitude and Longitude populated are returned.
    /// </summary>
    /// <param name="lat">Centre latitude (decimal degrees, WGS-84).</param>
    /// <param name="lng">Centre longitude (decimal degrees, WGS-84).</param>
    /// <param name="radiusKm">Search radius in kilometres (default 5, max 50).</param>
    [HttpGet("nearby")]
    [ProducesResponseType(typeof(List<VenueResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<VenueResponse>>> GetNearby(
        [FromQuery] double lat,
        [FromQuery] double lng,
        [FromQuery] double radiusKm = 5.0)
    {
        if (radiusKm <= 0 || radiusKm > 50)
            return BadRequest(new { message = "radiusKm must be between 0 and 50." });

        if (lat < -90 || lat > 90 || lng < -180 || lng > 180)
            return BadRequest(new { message = "Invalid coordinates." });

        // Load only venues that have coordinates — avoids pulling the whole table
        // into memory just to run Haversine (EF can't translate Math.Sin/Cos to SQL).
        var venuesWithCoords = await _db.Venues
            .Where(v => v.Latitude != null && v.Longitude != null)
            .Select(v => new
            {
                v.VenueId,
                v.VenueName,
                v.Address,
                v.Latitude,
                v.Longitude,
                v.OverallRating,
                v.OpenTime,
                v.CloseTime,
                v.PhoneNumber,
            })
            .ToListAsync();

        // Haversine filter in memory
        const double earthRadiusKm = 6371.0;

        var nearby = venuesWithCoords
            .Select(v =>
            {
                var dLat = ToRad(v.Latitude!.Value - lat);
                var dLng = ToRad(v.Longitude!.Value - lng);
                var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                      + Math.Cos(ToRad(lat)) * Math.Cos(ToRad(v.Latitude.Value))
                      * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
                var distKm = 2 * earthRadiusKm * Math.Asin(Math.Sqrt(a));
                return (Venue: v, DistanceKm: distKm);
            })
            .Where(x => x.DistanceKm <= radiusKm)
            .OrderBy(x => x.DistanceKm)
            .Take(50)
            .Select(x => new VenueResponse
            {
                VenueId      = x.Venue.VenueId,
                VenueName    = x.Venue.VenueName,
                Address      = x.Venue.Address,
                Latitude     = x.Venue.Latitude!.Value,
                Longitude    = x.Venue.Longitude!.Value,
                OverallRating = x.Venue.OverallRating,
                OpenTime     = x.Venue.OpenTime.ToString("HH:mm"),
                CloseTime    = x.Venue.CloseTime.ToString("HH:mm"),
                PhoneNumber  = x.Venue.PhoneNumber,
                DistanceKm   = Math.Round(x.DistanceKm, 2),
            })
            .ToList();

        return Ok(nearby);
    }

    private static double ToRad(double degrees) => degrees * Math.PI / 180.0;
}
