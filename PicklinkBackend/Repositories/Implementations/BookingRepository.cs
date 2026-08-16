using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PicklinkBackend.Data;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;

namespace PicklinkBackend.Repositories.Implementations;

public class BookingRepository : IBookingRepository
{
    private static readonly string[] InactiveStatuses = ["Cancelled", "Expired"];
    private static readonly string[] InactiveBookingStatuses = ["Cancelled", "Expired"];
    private static readonly string[] TimedBookingStatuses = ["Holding", "MatchWaiting"];
    private static readonly string[] ActiveParticipantStatuses = ["Pending", "Approved", "Accepted"];

    private readonly ApplicationDbContext _dbContext;

    public BookingRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IQueryable<Booking> Bookings => _dbContext.Bookings;
    public IQueryable<BookingOperation> BookingOperations => _dbContext.BookingOperations;
    public IQueryable<BookingCheckInGroup> BookingCheckInGroups => _dbContext.BookingCheckInGroups;

    public Task<Booking?> GetByIdAsync(int bookingId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Bookings
            .Include(b => b.Court).ThenInclude(c => c.Venue)
            .Include(b => b.Slots)
            .Include(b => b.CheckInGroups)
            .Include(b => b.Payments)
            .SingleOrDefaultAsync(b => b.BookingId == bookingId, cancellationToken);
    }

    public Task<Booking?> GetOwnedBookingAsync(int bookingId, int userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Bookings
            .Include(b => b.Court).ThenInclude(c => c.Venue)
            .Include(b => b.Slots).ThenInclude(s => s.Court)
            .Include(b => b.CheckInGroups).ThenInclude(g => g.Court)
            .Include(b => b.Payments).ThenInclude(p => p.StatusHistories)
            .Include(b => b.StatusHistories)
            .Include(b => b.Operation)
            .SingleOrDefaultAsync(b => b.BookingId == bookingId && b.Player != null && b.Player.UserId == userId, cancellationToken);
    }

    public Task<Booking?> GetOwnedBookingReadAsync(int bookingId, int userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Bookings.AsNoTracking()
            .AsSplitQuery()
            .Include(b => b.Court).ThenInclude(c => c.Venue)
            .Include(b => b.Slots).ThenInclude(s => s.Court)
            .Include(b => b.CheckInGroups).ThenInclude(g => g.Court)
            .Include(b => b.Payments).ThenInclude(p => p.StatusHistories)
            .Include(b => b.StatusHistories)
            .Include(b => b.Operation)
            .Include(b => b.Ratings)
            .SingleOrDefaultAsync(b => b.BookingId == bookingId && b.Player != null && b.Player.UserId == userId, cancellationToken);
    }

    public Task<List<Booking>> GetOverlappingBookingsAsync(int venueId, DateTime dayStart, DateTime dayEnd, DateTime now, CancellationToken cancellationToken = default)
    {
        return _dbContext.Bookings.AsNoTracking()
            .Where(booking => booking.Court.VenueId == venueId && booking.StartTime < dayEnd && booking.EndTime > dayStart &&
                !InactiveStatuses.Contains(booking.Status) && (booking.Status != "Holding" || booking.HoldExpiresAt > now || booking.HoldRemainingSeconds.HasValue))
            .Include(booking => booking.Player)
            .Include(booking => booking.Slots)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Booking>> GetStaleHoldingsAsync(List<int> courtIds, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        return _dbContext.Bookings
            .Include(booking => booking.Payments).ThenInclude(payment => payment.StatusHistories)
            .Where(booking => courtIds.Contains(booking.CourtId)
                && booking.Status == "Holding"
                && booking.HoldExpiresAt <= utcNow
                && !booking.Payments.Any(payment => payment.Status == "WaitingForConfirmation"))
            .ToListAsync(cancellationToken);
    }

    public Task<List<Booking>> GetPotentiallyOverlappingBookingsAsync(List<int> courtIds, DateTime firstStartTime, DateTime lastEndTime, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        return _dbContext.Bookings
            .Where(booking =>
                !InactiveStatuses.Contains(booking.Status) &&
                (booking.Status != "Holding" || booking.HoldExpiresAt > utcNow || booking.HoldRemainingSeconds.HasValue) &&
                booking.StartTime < lastEndTime && booking.EndTime > firstStartTime &&
                (courtIds.Contains(booking.CourtId) || booking.Slots.Any(existingSlot => courtIds.Contains(existingSlot.CourtId))))
            .Include(booking => booking.Slots)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        await _dbContext.Bookings.AddAsync(booking, cancellationToken);
    }

    public async Task AddBookingOperationAsync(BookingOperation operation, CancellationToken cancellationToken = default)
    {
        await _dbContext.BookingOperations.AddAsync(operation, cancellationToken);
    }

    public async Task AddBookingCheckInGroupAsync(BookingCheckInGroup group, CancellationToken cancellationToken = default)
    {
        await _dbContext.BookingCheckInGroups.AddAsync(group, cancellationToken);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        return _dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Player?> GetPlayerByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Players
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.Prestige)
            .ThenByDescending(item => item.SkillLevel)
            .ThenByDescending(item => item.PlayerId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddPlayerAsync(Player player, CancellationToken cancellationToken = default)
    {
        await _dbContext.Players.AddAsync(player, cancellationToken);
    }

    public Task<FavoriteVenue?> GetFavoriteVenueAsync(int playerId, int venueId, CancellationToken cancellationToken = default)
    {
        return _dbContext.FavoriteVenues
            .SingleOrDefaultAsync(item => item.PlayerId == playerId && item.VenueId == venueId, cancellationToken);
    }

    public async Task AddFavoriteVenueAsync(FavoriteVenue favoriteVenue, CancellationToken cancellationToken = default)
    {
        await _dbContext.FavoriteVenues.AddAsync(favoriteVenue, cancellationToken);
    }

    public Task RemoveFavoriteVenueAsync(FavoriteVenue favoriteVenue, CancellationToken cancellationToken = default)
    {
        _dbContext.FavoriteVenues.Remove(favoriteVenue);
        return Task.CompletedTask;
    }

    public Task<List<int>> GetFavoriteVenueIdsAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.FavoriteVenues.AsNoTracking()
            .Where(item => item.Player.UserId == userId)
            .Select(item => item.VenueId)
            .ToListAsync(cancellationToken);
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

    public async Task<IReadOnlyList<PlayerScheduleEntry>> LoadScheduleEntriesAsync(
        int userId,
        DateTime rangeStart,
        DateTime rangeEnd,
        CancellationToken cancellationToken = default)
    {
        // Holds expire on UTC wall-clock while play times are stored as Vietnam local time, so the two
        // are compared against different "now" values here — same split as LoadConflictDetailsAsync.
        var utcNow = DateTime.UtcNow;

        var ownedGroups = await _dbContext.BookingCheckInGroups.AsNoTracking()
            .Where(group =>
                group.Booking.Player != null
                && group.Booking.Player.UserId == userId
                && group.Booking.MatchId == null
                && group.Booking.OwnerEntryType == null
                && !InactiveBookingStatuses.Contains(group.Booking.Status)
                && (!TimedBookingStatuses.Contains(group.Booking.Status) || group.Booking.HoldExpiresAt > utcNow)
                && group.StartTime < rangeEnd
                && group.EndTime > rangeStart)
            .Select(group => new PlayerScheduleEntry(
                "Booking",
                group.BookingId,
                group.BookingId,
                group.StartTime,
                group.EndTime,
                group.Court.VenueId,
                group.Court.Venue.VenueName,
                group.Court.Venue.Address,
                group.CourtId,
                group.Court.CourtNumber,
                group.Booking.Title,
                group.Booking.Status,
                group.Booking.Payments
                    .Where(payment => payment.Payer.UserId == userId)
                    .OrderByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.Status)
                    .FirstOrDefault() ?? "Pending",
                group.Booking.TotalAmount,
                group.Booking.BookingCode,
                null))
            .ToListAsync(cancellationToken);

        var ownedLegacy = await _dbContext.Bookings.AsNoTracking()
            .Where(booking =>
                booking.Player != null
                && booking.Player.UserId == userId
                && booking.MatchId == null
                && booking.OwnerEntryType == null
                && !booking.CheckInGroups.Any()
                && !InactiveBookingStatuses.Contains(booking.Status)
                && (!TimedBookingStatuses.Contains(booking.Status) || booking.HoldExpiresAt > utcNow)
                && booking.StartTime < rangeEnd
                && booking.EndTime > rangeStart)
            .Select(booking => new PlayerScheduleEntry(
                "Booking",
                booking.BookingId,
                booking.BookingId,
                booking.StartTime,
                booking.EndTime,
                booking.Court.VenueId,
                booking.Court.Venue.VenueName,
                booking.Court.Venue.Address,
                booking.CourtId,
                booking.Court.CourtNumber,
                booking.Title,
                booking.Status,
                booking.Payments
                    .Where(payment => payment.Payer.UserId == userId)
                    .OrderByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.Status)
                    .FirstOrDefault() ?? "Pending",
                booking.TotalAmount,
                booking.BookingCode,
                null))
            .ToListAsync(cancellationToken);

        // A match booking is paid per participant, so Amount here is this player's own share.
        var matchGroups = await _dbContext.BookingCheckInGroups.AsNoTracking()
            .Where(group =>
                group.Booking.MatchId.HasValue
                && group.Booking.Match!.MatchParticipants.Any(participant =>
                    participant.Player.UserId == userId
                    && ActiveParticipantStatuses.Contains(participant.Status))
                && !InactiveBookingStatuses.Contains(group.Booking.Status)
                && (!TimedBookingStatuses.Contains(group.Booking.Status) || group.Booking.HoldExpiresAt > utcNow)
                && group.StartTime < rangeEnd
                && group.EndTime > rangeStart)
            .Select(group => new PlayerScheduleEntry(
                "Match",
                group.Booking.MatchId!.Value,
                group.BookingId,
                group.StartTime,
                group.EndTime,
                group.Court.VenueId,
                group.Court.Venue.VenueName,
                group.Court.Venue.Address,
                group.CourtId,
                group.Court.CourtNumber,
                group.Booking.Match!.Title,
                group.Booking.Status,
                group.Booking.Payments
                    .Where(payment => payment.Payer.UserId == userId)
                    .OrderByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.Status)
                    .FirstOrDefault() ?? "Pending",
                group.Booking.Payments
                    .Where(payment => payment.Payer.UserId == userId)
                    .OrderByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.Amount)
                    .FirstOrDefault(),
                group.Booking.BookingCode,
                group.Booking.Match!.MatchType))
            .ToListAsync(cancellationToken);

        var matchLegacy = await _dbContext.Bookings.AsNoTracking()
            .Where(booking =>
                booking.MatchId.HasValue
                && !booking.CheckInGroups.Any()
                && booking.Match!.MatchParticipants.Any(participant =>
                    participant.Player.UserId == userId
                    && ActiveParticipantStatuses.Contains(participant.Status))
                && !InactiveBookingStatuses.Contains(booking.Status)
                && (!TimedBookingStatuses.Contains(booking.Status) || booking.HoldExpiresAt > utcNow)
                && booking.StartTime < rangeEnd
                && booking.EndTime > rangeStart)
            .Select(booking => new PlayerScheduleEntry(
                "Match",
                booking.MatchId!.Value,
                booking.BookingId,
                booking.StartTime,
                booking.EndTime,
                booking.Court.VenueId,
                booking.Court.Venue.VenueName,
                booking.Court.Venue.Address,
                booking.CourtId,
                booking.Court.CourtNumber,
                booking.Match!.Title,
                booking.Status,
                booking.Payments
                    .Where(payment => payment.Payer.UserId == userId)
                    .OrderByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.Status)
                    .FirstOrDefault() ?? "Pending",
                booking.Payments
                    .Where(payment => payment.Payer.UserId == userId)
                    .OrderByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.Amount)
                    .FirstOrDefault(),
                booking.BookingCode,
                booking.Match!.MatchType))
            .ToListAsync(cancellationToken);

        // Ticket-session bookings are owner-held blocks with no slots or check-in groups of their own,
        // so the session's single booking range is the whole entry.
        var tickets = await _dbContext.SessionTickets.AsNoTracking()
            .Where(ticket =>
                ticket.Player.User.UserId == userId
                && (ticket.Status == "Paid"
                    || ticket.Status == "CheckedIn"
                    || ticket.Status == "PendingPayment" && ticket.HoldExpiresAt > utcNow)
                && ticket.TicketSession.Status == "Published"
                && ticket.TicketSession.Booking.StartTime < rangeEnd
                && ticket.TicketSession.Booking.EndTime > rangeStart)
            .Select(ticket => new PlayerScheduleEntry(
                "Ticket",
                ticket.SessionTicketId,
                ticket.TicketSession.BookingId,
                ticket.TicketSession.Booking.StartTime,
                ticket.TicketSession.Booking.EndTime,
                ticket.TicketSession.Booking.Court.VenueId,
                ticket.TicketSession.Booking.Court.Venue.VenueName,
                ticket.TicketSession.Booking.Court.Venue.Address,
                ticket.TicketSession.Booking.CourtId,
                ticket.TicketSession.Booking.Court.CourtNumber,
                ticket.TicketSession.Title,
                ticket.Status,
                ticket.Payment.Status,
                ticket.Payment.Amount,
                ticket.TicketCode,
                null))
            .ToListAsync(cancellationToken);

        return MergeContiguous(ownedGroups
            .Concat(ownedLegacy)
            .Concat(matchGroups)
            .Concat(matchLegacy)
            .Concat(tickets));
    }

    /// <summary>
    /// Folds back-to-back blocks of the same booking on the same court into one entry.
    ///
    /// Booking a court for 06:00-07:00 as two half-hour slots is one stretch of play, and the player
    /// reads it as one line. Check-in groups usually carry that shape already, but a booking that
    /// covers several courts at once can interleave its slots by time and split a court's run across
    /// groups, so the calendar re-joins them on the way out. Only exactly-touching blocks merge — a
    /// real gap between two runs stays two entries, because the court is genuinely free in between.
    /// </summary>
    private static IReadOnlyList<PlayerScheduleEntry> MergeContiguous(IEnumerable<PlayerScheduleEntry> entries)
    {
        var merged = new List<PlayerScheduleEntry>();

        foreach (var entry in entries
            .OrderBy(item => item.EntryType)
            .ThenBy(item => item.BookingId)
            .ThenBy(item => item.CourtId)
            .ThenBy(item => item.StartTime))
        {
            var previous = merged.Count > 0 ? merged[^1] : null;
            if (previous is not null
                && previous.EntryType == entry.EntryType
                && previous.BookingId == entry.BookingId
                && previous.CourtId == entry.CourtId
                && previous.EndTime == entry.StartTime)
            {
                merged[^1] = previous with { EndTime = entry.EndTime };
                continue;
            }

            merged.Add(entry);
        }

        return merged
            .OrderBy(entry => entry.StartTime)
            .ThenBy(entry => entry.EndTime)
            .ThenBy(entry => entry.VenueName)
            .ThenBy(entry => entry.CourtNumber)
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

        var replacementConflict = await _dbContext.MatchSlotReplacementRequests.AsNoTracking().AnyAsync(request =>
            request.PlayerId == playerId
            && request.Status == "Approved"
            && (!excludedMatchId.HasValue || request.MatchSlotAbsence.MatchId != excludedMatchId.Value)
            && !InactiveBookingStatuses.Contains(request.MatchSlotAbsence.BookingCheckInGroup.Booking.Status)
            && (!excludedBookingId.HasValue
                || request.MatchSlotAbsence.BookingCheckInGroup.BookingId != excludedBookingId.Value)
            && request.MatchSlotAbsence.BookingCheckInGroup.StartTime < endTime
            && request.MatchSlotAbsence.BookingCheckInGroup.EndTime > startTime,
            cancellationToken);
        if (replacementConflict) return true;

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

    public Task<RatingHistory?> GetBookingRatingAsync(int bookingId, int userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.RatingHistories.AsNoTracking()
            .Include(item => item.Booking).ThenInclude(item => item!.Court).ThenInclude(item => item.Venue)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.UserId == userId, cancellationToken);
    }

    public Task<Booking?> GetBookingForReviewAsync(int bookingId, int userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Bookings
            .Include(item => item.Player)
            .Include(item => item.Court).ThenInclude(item => item.Venue)
            .Include(item => item.Operation)
            .Include(item => item.Ratings)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && (
                item.Player != null && item.Player.UserId == userId
                || item.MatchId.HasValue && item.Match!.MatchParticipants.Any(participant =>
                    participant.Player.UserId == userId
                    && (participant.Status == "Approved" || participant.Status == "Accepted")
                    && item.Payments.Any(payment => payment.PayerId == participant.PlayerId && payment.Status == "Paid"))),
                cancellationToken);
    }

    public async Task AddRatingAsync(RatingHistory rating, CancellationToken cancellationToken = default)
    {
        await _dbContext.RatingHistories.AddAsync(rating, cancellationToken);
    }

    public async Task UpdateVenueOverallRatingAsync(int venueId, CancellationToken cancellationToken = default)
    {
        var venue = await _dbContext.Venues.SingleOrDefaultAsync(v => v.VenueId == venueId, cancellationToken);
        if (venue != null)
        {
            venue.OverallRating = await _dbContext.RatingHistories.AsNoTracking()
                .Where(item => item.TargetType == "Venue" && item.TargetId == venueId)
                .AverageAsync(item => (double)item.Score, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public Task<List<int>> GetStaleHoldingBookingIdsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        return _dbContext.Bookings.AsNoTracking()
            .Where(booking =>
                booking.Status == "Holding"
                && booking.HoldExpiresAt <= now
                && (booking.MatchId.HasValue
                    ? booking.Payments.Any(payment => payment.Status == "Pending")
                    : !booking.Payments.Any(payment => payment.Status == "WaitingForConfirmation")))
            .OrderBy(booking => booking.HoldExpiresAt)
            .Select(booking => booking.BookingId)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public Task<Booking?> GetHoldingBookingForExpirationAsync(int bookingId, DateTime now, CancellationToken cancellationToken = default)
    {
        return _dbContext.Bookings
            .Include(item => item.Court)
            .Include(item => item.Slots).ThenInclude(slot => slot.Court)
            .Include(item => item.Payments).ThenInclude(payment => payment.StatusHistories)
            .Include(item => item.Match).ThenInclude(match => match!.MatchParticipants).ThenInclude(participant => participant.Player)
            .SingleOrDefaultAsync(item =>
                item.BookingId == bookingId
                && item.Status == "Holding"
                && item.HoldExpiresAt <= now
                && (item.MatchId.HasValue
                    ? item.Payments.Any(payment => payment.Status == "Pending")
                    : !item.Payments.Any(payment => payment.Status == "WaitingForConfirmation")),
                cancellationToken);
    }

    public async Task AddBookingStatusHistoryAsync(BookingStatusHistory history, CancellationToken cancellationToken = default)
    {
        await _dbContext.BookingStatusHistories.AddAsync(history, cancellationToken);
    }

    public async Task<List<(int SessionTicketId, int TicketSessionId)>> GetStaleSessionTicketCandidatesAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var candidates = await _dbContext.SessionTickets.AsNoTracking()
            .Where(ticket =>
                ticket.Status == "PendingPayment"
                && ticket.Payment.Status == "Pending"
                && (!ticket.HoldExpiresAt.HasValue || ticket.HoldExpiresAt <= now))
            .OrderBy(ticket => ticket.HoldExpiresAt)
            .ThenBy(ticket => ticket.SessionTicketId)
            .Select(ticket => new { ticket.SessionTicketId, ticket.TicketSessionId })
            .Take(100)
            .ToListAsync(cancellationToken);

        return candidates.Select(c => (c.SessionTicketId, c.TicketSessionId)).ToList();
    }

    public Task<SessionTicket?> GetSessionTicketForExpirationAsync(int sessionTicketId, DateTime now, CancellationToken cancellationToken = default)
    {
        return _dbContext.SessionTickets
            .Include(item => item.Payment).ThenInclude(item => item.StatusHistories)
            .Include(item => item.TicketSession).ThenInclude(item => item.Booking)
                .ThenInclude(item => item.Court)
            .SingleOrDefaultAsync(item =>
                item.SessionTicketId == sessionTicketId
                && item.Status == "PendingPayment"
                && item.Payment.Status == "Pending"
                && (!item.HoldExpiresAt.HasValue || item.HoldExpiresAt <= now),
                cancellationToken);
    }

    public IQueryable<Booking> GetMyBookingsQueryable(int userId)
    {
        return _dbContext.Bookings.AsNoTracking()
            .AsSplitQuery()
            .Where(booking => booking.Player != null
                && booking.Player.UserId == userId
                && booking.Payments.Any(payment => payment.Payer.UserId == userId
                    && (payment.SubmittedAt.HasValue || payment.PaidAt.HasValue)));
    }

    public Task<List<Booking>> GetHoldingGroupBookingsAsync(Guid paymentGroupId, int userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Bookings.AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Court).ThenInclude(item => item.Venue)
            .Include(item => item.Payments).ThenInclude(item => item.StatusHistories)
            .Include(item => item.StatusHistories)
            .Include(item => item.Operation)
            .Include(item => item.Ratings)
            .Where(item => item.Player!.UserId == userId && item.Payments.Any(payment => payment.PaymentGroupId == paymentGroupId))
            .OrderBy(item => item.StartTime)
            .ThenBy(item => item.CourtId)
            .ToListAsync(cancellationToken);
    }
}
