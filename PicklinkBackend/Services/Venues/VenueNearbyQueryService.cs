using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Venues;

public sealed class VenueNearbyQueryService
{
    private readonly ApplicationDbContext _dbContext;

    public VenueNearbyQueryService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<VenueResponse>> GetNearbyAsync(
        double lat,
        double lng,
        double radiusKm,
        CancellationToken cancellationToken)
    {
        var venuesWithCoords = await _dbContext.Venues
            .Where(venue => venue.ApprovalStatus == "Approved"
                && venue.Latitude != null
                && venue.Longitude != null)
            .Select(venue => new
            {
                venue.VenueId,
                venue.VenueName,
                venue.Address,
                venue.Latitude,
                venue.Longitude,
                venue.OverallRating,
                venue.OpenTime,
                venue.CloseTime,
                venue.PhoneNumber,
            })
            .ToListAsync(cancellationToken);

        const double earthRadiusKm = 6371.0;

        return venuesWithCoords
            .Select(venue =>
            {
                var dLat = ToRad(venue.Latitude!.Value - lat);
                var dLng = ToRad(venue.Longitude!.Value - lng);
                var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                      + Math.Cos(ToRad(lat)) * Math.Cos(ToRad(venue.Latitude.Value))
                      * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
                var distKm = 2 * earthRadiusKm * Math.Asin(Math.Sqrt(a));
                return (Venue: venue, DistanceKm: distKm);
            })
            .Where(item => item.DistanceKm <= radiusKm)
            .OrderBy(item => item.DistanceKm)
            .Take(50)
            .Select(item => new VenueResponse
            {
                VenueId = item.Venue.VenueId,
                VenueName = item.Venue.VenueName,
                Address = item.Venue.Address,
                Latitude = item.Venue.Latitude!.Value,
                Longitude = item.Venue.Longitude!.Value,
                OverallRating = item.Venue.OverallRating,
                OpenTime = item.Venue.OpenTime.ToString("HH:mm"),
                CloseTime = item.Venue.CloseTime.ToString("HH:mm"),
                PhoneNumber = item.Venue.PhoneNumber,
                DistanceKm = Math.Round(item.DistanceKm, 2),
            })
            .ToList();
    }


    private static double ToRad(double degrees) => degrees * Math.PI / 180.0;
}