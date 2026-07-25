namespace PicklinkBackend.Services.Bookings;

public sealed record PlayerScheduleConflictDetail(
    string ConflictType,
    string Source,
    int? BookingId,
    int? MatchId,
    string? Title,
    DateTime StartTime,
    DateTime EndTime,
    int CourtId,
    int CourtNumber,
    int VenueId,
    string VenueName)
{
    public PlayerScheduleConflictDetail(string venueName, int courtNumber, DateTime startTime, DateTime endTime)
        : this("Booking", "Booking", null, null, null, startTime, endTime, 0, courtNumber, 0, venueName)
    {
    }
}
