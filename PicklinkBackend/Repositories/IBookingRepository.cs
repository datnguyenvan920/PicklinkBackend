using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;

namespace PicklinkBackend.Repositories;

public interface IBookingRepository
{
    IQueryable<Booking> Bookings { get; }
    IQueryable<BookingOperation> BookingOperations { get; }
    IQueryable<BookingCheckInGroup> BookingCheckInGroups { get; }

    Task<Booking?> GetByIdAsync(int bookingId, CancellationToken cancellationToken = default);
    Task<Booking?> GetOwnedBookingAsync(int bookingId, int userId, CancellationToken cancellationToken = default);
    Task<Booking?> GetOwnedBookingReadAsync(int bookingId, int userId, CancellationToken cancellationToken = default);
    Task<List<Booking>> GetOverlappingBookingsAsync(int venueId, DateTime dayStart, DateTime dayEnd, DateTime now, CancellationToken cancellationToken = default);
    Task<List<Booking>> GetStaleHoldingsAsync(List<int> courtIds, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<List<Booking>> GetPotentiallyOverlappingBookingsAsync(List<int> courtIds, DateTime firstStartTime, DateTime lastEndTime, DateTime utcNow, CancellationToken cancellationToken = default);
    Task AddAsync(Booking booking, CancellationToken cancellationToken = default);
    Task AddBookingOperationAsync(BookingOperation operation, CancellationToken cancellationToken = default);
    Task AddBookingCheckInGroupAsync(BookingCheckInGroup group, CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<Player?> GetPlayerByUserIdAsync(int userId, CancellationToken cancellationToken = default);
    Task AddPlayerAsync(Player player, CancellationToken cancellationToken = default);
    Task<FavoriteVenue?> GetFavoriteVenueAsync(int playerId, int venueId, CancellationToken cancellationToken = default);
    Task AddFavoriteVenueAsync(FavoriteVenue favoriteVenue, CancellationToken cancellationToken = default);
    Task RemoveFavoriteVenueAsync(FavoriteVenue favoriteVenue, CancellationToken cancellationToken = default);
    Task<List<int>> GetFavoriteVenueIdsAsync(int userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlayerScheduleConflictDetail>> LoadConflictDetailsAsync(
        int playerId,
        DateTime rangeStart,
        DateTime rangeEnd,
        int? excludedBookingId = null,
        int? excludedMatchId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlayerScheduleEntry>> LoadScheduleEntriesAsync(
        int userId,
        DateTime rangeStart,
        DateTime rangeEnd,
        CancellationToken cancellationToken = default);

    Task<Dictionary<int, List<(DateTime StartTime, DateTime EndTime)>>> LoadBusyPeriodsAsync(
        IEnumerable<int> playerIds,
        DateTime rangeStart,
        DateTime rangeEnd,
        int? excludedBookingId = null,
        int? excludedMatchId = null,
        CancellationToken cancellationToken = default);

    Task<bool> HasConflictAsync(
        int playerId,
        DateTime startTime,
        DateTime endTime,
        int? excludedBookingId = null,
        int? excludedMatchId = null,
        CancellationToken cancellationToken = default);

    Task<RatingHistory?> GetBookingRatingAsync(int bookingId, int userId, CancellationToken cancellationToken = default);
    Task<RatingHistory?> GetVenueRatingAsync(int venueId, int userId, bool tracking, CancellationToken cancellationToken = default);
    Task<Booking?> GetBookingForReviewAsync(int bookingId, int userId, CancellationToken cancellationToken = default);
    Task AddRatingAsync(RatingHistory rating, CancellationToken cancellationToken = default);
    Task UpdateVenueOverallRatingAsync(int venueId, CancellationToken cancellationToken = default);

    Task<List<int>> GetStaleHoldingBookingIdsAsync(DateTime now, CancellationToken cancellationToken = default);
    Task<Booking?> GetHoldingBookingForExpirationAsync(int bookingId, DateTime now, CancellationToken cancellationToken = default);
    Task AddBookingStatusHistoryAsync(BookingStatusHistory history, CancellationToken cancellationToken = default);
    Task<List<(int SessionTicketId, int TicketSessionId)>> GetStaleSessionTicketCandidatesAsync(DateTime now, CancellationToken cancellationToken = default);
    Task<SessionTicket?> GetSessionTicketForExpirationAsync(int sessionTicketId, DateTime now, CancellationToken cancellationToken = default);

    IQueryable<Booking> GetMyBookingsQueryable(int userId);
    Task<List<Booking>> GetHoldingGroupBookingsAsync(Guid paymentGroupId, int userId, CancellationToken cancellationToken = default);
}
