namespace PicklinkBackend.Services.Bookings;

/// <summary>
/// One contiguous block of court time a player is committed to, from any of the three sources a
/// player can end up on a court: a booking they made, a ticket they bought, or a match they joined.
///
/// The calendar unit is the check-in group (one court, one unbroken stretch), not the booking:
/// a single booking spanning two courts or two separate hours is two entries on the calendar,
/// which is what the player actually has to show up for.
/// </summary>
public sealed record PlayerScheduleEntry(
    string EntryType,
    int ReferenceId,
    int BookingId,
    DateTime StartTime,
    DateTime EndTime,
    int VenueId,
    string VenueName,
    string Address,
    int CourtId,
    int CourtNumber,
    string? Title,
    string Status,
    string PaymentStatus,
    decimal Amount,
    string? Code,
    string? MatchType);
