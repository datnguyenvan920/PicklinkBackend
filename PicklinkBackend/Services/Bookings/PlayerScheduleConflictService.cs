using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;

namespace PicklinkBackend.Services.Bookings;

public sealed record PlayerScheduleConflictDetail(
    string VenueName,
    int CourtNumber,
    DateTime StartTime,
    DateTime EndTime);

public sealed class PlayerScheduleConflictService
{
    private static readonly string[] InactiveBookingStatuses = ["Cancelled", "Expired"];
    private static readonly string[] TimedBookingStatuses = ["Holding", "MatchWaiting"];
    private static readonly string[] ActiveParticipantStatuses = ["Pending", "Approved", "Accepted"];

    private readonly ApplicationDbContext _dbContext;

    public PlayerScheduleConflictService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PlayerScheduleConflictDetail>> LoadConflictDetailsAsync(
        int playerId,
        DateTime rangeStart,
        DateTime rangeEnd,
        int? excludedBookingId = null,
        int? excludedMatchId = null,
        CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        // A booking can contain slots on separate days. Its StartTime/EndTime is only an envelope,
        // so using it here would incorrectly make every moment between those slots look occupied.
        var ownedBookingSlots = await _dbContext.BookingSlots.AsNoTracking()
            .Where(slot =>
                slot.Booking.PlayerId == playerId
                && (!excludedBookingId.HasValue || slot.BookingId != excludedBookingId.Value)
                && (!excludedMatchId.HasValue || slot.Booking.MatchId != excludedMatchId.Value)
                && !InactiveBookingStatuses.Contains(slot.Booking.Status)
                && (!TimedBookingStatuses.Contains(slot.Booking.Status) || slot.Booking.HoldExpiresAt > utcNow)
                && slot.StartTime < rangeEnd
                && slot.EndTime > rangeStart)
            .Select(slot => new PlayerScheduleConflictDetail(
                slot.Court.Venue.VenueName,
                slot.Court.CourtNumber,
                slot.StartTime,
                slot.EndTime))
            .ToListAsync(cancellationToken);
        var legacyOwnedBookings = await _dbContext.Bookings.AsNoTracking()
            .Where(booking =>
                booking.PlayerId == playerId
                && !booking.Slots.Any()
                && (!excludedBookingId.HasValue || booking.BookingId != excludedBookingId.Value)
                && (!excludedMatchId.HasValue || booking.MatchId != excludedMatchId.Value)
                && !InactiveBookingStatuses.Contains(booking.Status)
                && (!TimedBookingStatuses.Contains(booking.Status) || booking.HoldExpiresAt > utcNow)
                && booking.StartTime < rangeEnd
                && booking.EndTime > rangeStart)
            .Select(booking => new PlayerScheduleConflictDetail(
                booking.Court.Venue.VenueName,
                booking.Court.CourtNumber,
                booking.StartTime,
                booking.EndTime))
            .ToListAsync(cancellationToken);

        var matchBookingSlots = await _dbContext.BookingSlots.AsNoTracking()
            .Where(slot =>
                slot.Booking.MatchId.HasValue
                && slot.Booking.Match!.MatchParticipants.Any(participant =>
                    participant.PlayerId == playerId
                    && ActiveParticipantStatuses.Contains(participant.Status)
                    && (!excludedMatchId.HasValue || participant.MatchId != excludedMatchId.Value))
                && (!excludedBookingId.HasValue || slot.BookingId != excludedBookingId.Value)
                && !InactiveBookingStatuses.Contains(slot.Booking.Status)
                && (!TimedBookingStatuses.Contains(slot.Booking.Status) || slot.Booking.HoldExpiresAt > utcNow)
                && slot.StartTime < rangeEnd
                && slot.EndTime > rangeStart)
            .Select(slot => new PlayerScheduleConflictDetail(
                slot.Court.Venue.VenueName,
                slot.Court.CourtNumber,
                slot.StartTime,
                slot.EndTime))
            .ToListAsync(cancellationToken);
        var legacyMatchBookings = await _dbContext.MatchParticipants.AsNoTracking()
            .Where(participant =>
                participant.PlayerId == playerId
                && ActiveParticipantStatuses.Contains(participant.Status)
                && (!excludedMatchId.HasValue || participant.MatchId != excludedMatchId.Value))
            .SelectMany(participant => participant.Match.Bookings.Where(booking =>
                !booking.Slots.Any()
                && (!excludedBookingId.HasValue || booking.BookingId != excludedBookingId.Value)
                && !InactiveBookingStatuses.Contains(booking.Status)
                && (!TimedBookingStatuses.Contains(booking.Status) || booking.HoldExpiresAt > utcNow)
                && booking.StartTime < rangeEnd
                && booking.EndTime > rangeStart))
            .Select(booking => new PlayerScheduleConflictDetail(
                booking.Court.Venue.VenueName,
                booking.Court.CourtNumber,
                booking.StartTime,
                booking.EndTime))
            .ToListAsync(cancellationToken);

        var ticketBookingSlots = await _dbContext.SessionTickets.AsNoTracking()
            .Where(ticket =>
                ticket.PlayerId == playerId
                && (ticket.Status == "Paid"
                    || ticket.Status == "CheckedIn"
                    || ticket.Status == "PendingPayment" && ticket.HoldExpiresAt > utcNow)
                && ticket.TicketSession.Status == "Published"
                && (!excludedBookingId.HasValue
                    || ticket.TicketSession.BookingId != excludedBookingId.Value))
            .SelectMany(ticket => ticket.TicketSession.Booking.Slots.Where(slot =>
                slot.StartTime < rangeEnd && slot.EndTime > rangeStart))
            .Select(slot => new PlayerScheduleConflictDetail(
                slot.Court.Venue.VenueName,
                slot.Court.CourtNumber,
                slot.StartTime,
                slot.EndTime))
            .ToListAsync(cancellationToken);
        var legacyTicketBookings = await _dbContext.SessionTickets.AsNoTracking()
            .Where(ticket =>
                ticket.PlayerId == playerId
                && (ticket.Status == "Paid"
                    || ticket.Status == "CheckedIn"
                    || ticket.Status == "PendingPayment" && ticket.HoldExpiresAt > utcNow)
                && ticket.TicketSession.Status == "Published"
                && !ticket.TicketSession.Booking.Slots.Any()
                && (!excludedBookingId.HasValue
                    || ticket.TicketSession.BookingId != excludedBookingId.Value)
                && ticket.TicketSession.Booking.StartTime < rangeEnd
                && ticket.TicketSession.Booking.EndTime > rangeStart)
            .Select(ticket => new PlayerScheduleConflictDetail(
                ticket.TicketSession.Booking.Court.Venue.VenueName,
                ticket.TicketSession.Booking.Court.CourtNumber,
                ticket.TicketSession.Booking.StartTime,
                ticket.TicketSession.Booking.EndTime))
            .ToListAsync(cancellationToken);

        return ownedBookingSlots
            .Concat(legacyOwnedBookings)
            .Concat(matchBookingSlots)
            .Concat(legacyMatchBookings)
            .Concat(ticketBookingSlots)
            .Concat(legacyTicketBookings)
            .Distinct()
            .OrderBy(item => item.StartTime)
            .ThenBy(item => item.EndTime)
            .ThenBy(item => item.VenueName)
            .ThenBy(item => item.CourtNumber)
            .ToArray();
    }
    public async Task<Dictionary<int, List<(DateTime StartTime, DateTime EndTime)>>> LoadBusyPeriodsAsync(
        IEnumerable<int> playerIds,
        DateTime rangeStart,
        DateTime rangeEnd,
        int? excludedBookingId = null,
        int? excludedMatchId = null,
        CancellationToken cancellationToken = default)
    {
        var ids = playerIds.Distinct().ToArray();
        if (ids.Length == 0) return [];

        var utcNow = DateTime.UtcNow;
        var ownedBookings = await _dbContext.BookingSlots.AsNoTracking()
            .Where(slot =>
                slot.Booking.PlayerId.HasValue
                && ids.Contains(slot.Booking.PlayerId.Value)
                && (!excludedBookingId.HasValue || slot.BookingId != excludedBookingId.Value)
                && (!excludedMatchId.HasValue || slot.Booking.MatchId != excludedMatchId.Value)
                && !InactiveBookingStatuses.Contains(slot.Booking.Status)
                && (!TimedBookingStatuses.Contains(slot.Booking.Status) || slot.Booking.HoldExpiresAt > utcNow)
                && slot.StartTime < rangeEnd
                && slot.EndTime > rangeStart)
            .Select(slot => new
            {
                PlayerId = slot.Booking.PlayerId!.Value,
                slot.StartTime,
                slot.EndTime
            })
            .ToListAsync(cancellationToken);
        var legacyOwnedBookings = await _dbContext.Bookings.AsNoTracking()
            .Where(booking =>
                booking.PlayerId.HasValue
                && ids.Contains(booking.PlayerId.Value)
                && !booking.Slots.Any()
                && (!excludedBookingId.HasValue || booking.BookingId != excludedBookingId.Value)
                && (!excludedMatchId.HasValue || booking.MatchId != excludedMatchId.Value)
                && !InactiveBookingStatuses.Contains(booking.Status)
                && (!TimedBookingStatuses.Contains(booking.Status) || booking.HoldExpiresAt > utcNow)
                && booking.StartTime < rangeEnd
                && booking.EndTime > rangeStart)
            .Select(booking => new { PlayerId = booking.PlayerId!.Value, booking.StartTime, booking.EndTime })
            .ToListAsync(cancellationToken);

        var matchBookings = await _dbContext.MatchParticipants.AsNoTracking()
            .Where(participant =>
                ids.Contains(participant.PlayerId)
                && ActiveParticipantStatuses.Contains(participant.Status)
                && (!excludedMatchId.HasValue || participant.MatchId != excludedMatchId.Value))
            .SelectMany(participant => participant.Match.Bookings
                .Where(booking =>
                    (!excludedBookingId.HasValue || booking.BookingId != excludedBookingId.Value)
                    && !InactiveBookingStatuses.Contains(booking.Status)
                    && (!TimedBookingStatuses.Contains(booking.Status) || booking.HoldExpiresAt > utcNow))
                .SelectMany(booking => booking.Slots
                    .Where(slot => slot.StartTime < rangeEnd && slot.EndTime > rangeStart)
                    .Select(slot => new { participant.PlayerId, slot.StartTime, slot.EndTime })))
            .ToListAsync(cancellationToken);
        var legacyMatchBookings = await _dbContext.MatchParticipants.AsNoTracking()
            .Where(participant =>
                ids.Contains(participant.PlayerId)
                && ActiveParticipantStatuses.Contains(participant.Status)
                && (!excludedMatchId.HasValue || participant.MatchId != excludedMatchId.Value))
            .SelectMany(participant => participant.Match.Bookings
                .Where(booking =>
                    !booking.Slots.Any()
                    && (!excludedBookingId.HasValue || booking.BookingId != excludedBookingId.Value)
                    && !InactiveBookingStatuses.Contains(booking.Status)
                    && (!TimedBookingStatuses.Contains(booking.Status) || booking.HoldExpiresAt > utcNow)
                    && booking.StartTime < rangeEnd
                    && booking.EndTime > rangeStart)
                .Select(booking => new { participant.PlayerId, booking.StartTime, booking.EndTime }))
            .ToListAsync(cancellationToken);

        var ticketBookings = await _dbContext.SessionTickets.AsNoTracking()
            .Where(ticket =>
                ids.Contains(ticket.PlayerId)
                && (ticket.Status == "Paid"
                    || ticket.Status == "CheckedIn"
                    || ticket.Status == "PendingPayment" && ticket.HoldExpiresAt > utcNow)
                && ticket.TicketSession.Status == "Published"
                && (!excludedBookingId.HasValue
                    || ticket.TicketSession.BookingId != excludedBookingId.Value))
            .SelectMany(ticket => ticket.TicketSession.Booking.Slots
                .Where(slot => slot.StartTime < rangeEnd && slot.EndTime > rangeStart)
                .Select(slot => new { ticket.PlayerId, slot.StartTime, slot.EndTime }))
            .ToListAsync(cancellationToken);
        var legacyTicketBookings = await _dbContext.SessionTickets.AsNoTracking()
            .Where(ticket =>
                ids.Contains(ticket.PlayerId)
                && (ticket.Status == "Paid"
                    || ticket.Status == "CheckedIn"
                    || ticket.Status == "PendingPayment" && ticket.HoldExpiresAt > utcNow)
                && ticket.TicketSession.Status == "Published"
                && !ticket.TicketSession.Booking.Slots.Any()
                && (!excludedBookingId.HasValue || ticket.TicketSession.BookingId != excludedBookingId.Value)
                && ticket.TicketSession.Booking.StartTime < rangeEnd
                && ticket.TicketSession.Booking.EndTime > rangeStart)
            .Select(ticket => new
            {
                ticket.PlayerId,
                ticket.TicketSession.Booking.StartTime,
                ticket.TicketSession.Booking.EndTime
            })
            .ToListAsync(cancellationToken);

        return ownedBookings
            .Concat(legacyOwnedBookings)
            .Concat(matchBookings)
            .Concat(legacyMatchBookings)
            .Concat(ticketBookings)
            .Concat(legacyTicketBookings)
            .Distinct()
            .GroupBy(booking => booking.PlayerId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(booking => (booking.StartTime, booking.EndTime))
                    .ToList());
    }

    public async Task<bool> HasConflictAsync(
        int playerId,
        DateTime startTime,
        DateTime endTime,
        int? excludedBookingId = null,
        int? excludedMatchId = null,
        CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;

        var ownedBookingConflict = await _dbContext.Bookings.AsNoTracking().AnyAsync(booking =>
            booking.PlayerId == playerId
            && (!excludedBookingId.HasValue || booking.BookingId != excludedBookingId.Value)
            && (!excludedMatchId.HasValue || booking.MatchId != excludedMatchId.Value)
            && !InactiveBookingStatuses.Contains(booking.Status)
            && (!TimedBookingStatuses.Contains(booking.Status) || booking.HoldExpiresAt > utcNow)
            && (booking.Slots.Any(slot => slot.StartTime < endTime && slot.EndTime > startTime)
                || !booking.Slots.Any() && booking.StartTime < endTime && booking.EndTime > startTime),
            cancellationToken);
        if (ownedBookingConflict) return true;

        var matchConflict = await _dbContext.MatchParticipants.AsNoTracking().AnyAsync(participant =>
            participant.PlayerId == playerId
            && ActiveParticipantStatuses.Contains(participant.Status)
            && (!excludedMatchId.HasValue || participant.MatchId != excludedMatchId.Value)
            && participant.Match.Bookings.Any(booking =>
                (!excludedBookingId.HasValue || booking.BookingId != excludedBookingId.Value)
                && !InactiveBookingStatuses.Contains(booking.Status)
                && (!TimedBookingStatuses.Contains(booking.Status) || booking.HoldExpiresAt > utcNow)
                && (booking.Slots.Any(slot => slot.StartTime < endTime && slot.EndTime > startTime)
                    || !booking.Slots.Any() && booking.StartTime < endTime && booking.EndTime > startTime)),
            cancellationToken);
        if (matchConflict) return true;

        return await _dbContext.SessionTickets.AsNoTracking().AnyAsync(ticket =>
            ticket.PlayerId == playerId
            && (ticket.Status == "Paid"
                || ticket.Status == "CheckedIn"
                || ticket.Status == "PendingPayment" && ticket.HoldExpiresAt > utcNow)
            && ticket.TicketSession.Status == "Published"
            && (!excludedBookingId.HasValue
                || ticket.TicketSession.BookingId != excludedBookingId.Value)
            && (ticket.TicketSession.Booking.Slots.Any(slot => slot.StartTime < endTime && slot.EndTime > startTime)
                || !ticket.TicketSession.Booking.Slots.Any()
                    && ticket.TicketSession.Booking.StartTime < endTime
                    && ticket.TicketSession.Booking.EndTime > startTime),
            cancellationToken);
    }
}
