using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;

namespace PicklinkBackend.Services;

public sealed class PlayerScheduleConflictService
{
    private static readonly string[] InactiveBookingStatuses = ["Cancelled", "Expired"];
    private static readonly string[] TimedBookingStatuses = ["Holding", "MatchWaiting"];
    private static readonly string[] ActiveParticipantStatuses = ["Pending", "Accepted"];

    private readonly ApplicationDbContext _dbContext;

    public PlayerScheduleConflictService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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
