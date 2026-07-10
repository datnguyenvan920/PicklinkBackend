namespace PicklinkBackend.DTOs;

/// <summary>
/// Returned by GET /api/venues/nearby.
/// Contains everything the Flutter map screen needs to render a venue pin
/// and populate the PrefferedVenue list for matchmaking.
/// </summary>
public class VenueResponse
{
    /// <summary>Database primary key Ã¢â‚¬â€ sent as PrefferedVenue entry in the matchmaking payload.</summary>
    public int VenueId { get; set; }

    public string VenueName { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>Overall rating (0Ã¢â‚¬â€œ5).</summary>
    public double OverallRating { get; set; }

    /// <summary>Opening time as "HH:mm" string, e.g. "06:00".</summary>
    public string OpenTime { get; set; } = string.Empty;

    /// <summary>Closing time as "HH:mm" string, e.g. "22:00".</summary>
    public string CloseTime { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Straight-line distance in kilometres from the query point (lat/lng).
    /// Populated by VenueController, not stored in the DB.
    /// </summary>
    public double DistanceKm { get; set; }
}
