using PicklinkBackend.Repositories;

namespace PicklinkBackend.Services.Bookings.Implementations;

public sealed class PlayerScheduleConflictService
{
    private readonly IBookingRepository _bookingRepository;

    public PlayerScheduleConflictService(IBookingRepository bookingRepository)
    {
        _bookingRepository = bookingRepository;
    }

    public Task<IReadOnlyList<PlayerScheduleConflictDetail>> LoadConflictDetailsAsync(
        int playerId,
        DateTime rangeStart,
        DateTime rangeEnd,
        int? excludedBookingId = null,
        int? excludedMatchId = null,
        CancellationToken cancellationToken = default) =>
        _bookingRepository.LoadConflictDetailsAsync(playerId, rangeStart, rangeEnd, excludedBookingId, excludedMatchId, cancellationToken);

    public Task<Dictionary<int, List<(DateTime StartTime, DateTime EndTime)>>> LoadBusyPeriodsAsync(
        IEnumerable<int> playerIds,
        DateTime rangeStart,
        DateTime rangeEnd,
        int? excludedBookingId = null,
        int? excludedMatchId = null,
        CancellationToken cancellationToken = default) =>
        _bookingRepository.LoadBusyPeriodsAsync(playerIds, rangeStart, rangeEnd, excludedBookingId, excludedMatchId, cancellationToken);

    public Task<bool> HasConflictAsync(
        int playerId,
        DateTime startTime,
        DateTime endTime,
        int? excludedBookingId = null,
        int? excludedMatchId = null,
        CancellationToken cancellationToken = default) =>
        _bookingRepository.HasConflictAsync(playerId, startTime, endTime, excludedBookingId, excludedMatchId, cancellationToken);
}
