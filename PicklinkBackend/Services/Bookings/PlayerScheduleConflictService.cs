using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;

namespace PicklinkBackend.Services.Bookings;

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
        var ownedBookings = await _dbContext.Bookings.AsNoTracking()
            .Where(booking =>
                booking.PlayerId.HasValue
                && ids.Contains(booking.PlayerId.Value)
                && (!excludedBookingId.HasValue || booking.BookingId != excludedBookingId.Value)
                && (!excludedMatchId.HasValue || booking.MatchId != excludedMatchId.Value)
                && !InactiveBookingStatuses.Contains(booking.Status)
                && (!TimedBookingStatuses.Contains(booking.Status) || booking.HoldExpiresAt > utcNow)
                && booking.StartTime < rangeEnd
                && booking.EndTime > rangeStart)
            .Select(booking => new
            {
                PlayerId = booking.PlayerId!.Value,
                booking.StartTime,
                booking.EndTime
            })
            .ToListAsync(cancellationToken);

        var matchBookings = await _dbContext.MatchParticipants.AsNoTracking()
            .Where(participant =>
                ids.Contains(participant.PlayerId)
                && ActiveParticipantStatuses.Contains(participant.Status)
                && (!excludedMatchId.HasValue || participant.MatchId != excludedMatchId.Value))
            .SelectMany(
                participant => participant.Match.Bookings.Where(booking =>
                    (!excludedBookingId.HasValue || booking.BookingId != excludedBookingId.Value)
                    && !InactiveBookingStatuses.Contains(booking.Status)
                    && (!TimedBookingStatuses.Contains(booking.Status) || booking.HoldExpiresAt > utcNow)
                    && booking.StartTime < rangeEnd
                    && booking.EndTime > rangeStart),
                (participant, booking) => new
                {
                    participant.PlayerId,
                    booking.StartTime,
                    booking.EndTime
                })
            .ToListAsync(cancellationToken);

        return ownedBookings
            .Concat(matchBookings)
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
            && booking.StartTime < endTime
            && booking.EndTime > startTime,
            cancellationToken);
        if (ownedBookingConflict) return true;

        return await _dbContext.MatchParticipants.AsNoTracking().AnyAsync(participant =>
            participant.PlayerId == playerId
            && ActiveParticipantStatuses.Contains(participant.Status)
            && (!excludedMatchId.HasValue || participant.MatchId != excludedMatchId.Value)
            && participant.Match.Bookings.Any(booking =>
                (!excludedBookingId.HasValue || booking.BookingId != excludedBookingId.Value)
                && !InactiveBookingStatuses.Contains(booking.Status)
                && (!TimedBookingStatuses.Contains(booking.Status) || booking.HoldExpiresAt > utcNow)
                && booking.StartTime < endTime
                && booking.EndTime > startTime),
            cancellationToken);
    }
}
