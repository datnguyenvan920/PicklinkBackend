using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PicklinkBackend.Data;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Notifications;

namespace PicklinkBackend.Repositories.Implementations;

public class PaymentRepository : IPaymentRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PaymentRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IQueryable<Payment> Payments => _dbContext.Payments;
    public IQueryable<SePayTransaction> SePayTransactions => _dbContext.SePayTransactions;
    public IQueryable<Booking> Bookings => _dbContext.Bookings;
    public IQueryable<TicketSession> TicketSessions => _dbContext.TicketSessions;
    public IQueryable<SessionTicket> SessionTickets => _dbContext.SessionTickets;
    public IQueryable<Match> Matches => _dbContext.Matches;
    public IQueryable<BookingSlot> BookingSlots => _dbContext.BookingSlots;
    public IQueryable<VenueAuditLog> VenueAuditLogs => _dbContext.VenueAuditLogs;
    public IQueryable<OwnerBankAccount> OwnerBankAccounts => _dbContext.OwnerBankAccounts;
    public IQueryable<Player> Players => _dbContext.Players;
    public IQueryable<Venue> Venues => _dbContext.Venues;
    public IQueryable<Staff> Staff => _dbContext.Staff;

    public Task<Payment?> GetByIdAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Payments
            .Include(p => p.StatusHistories)
            .SingleOrDefaultAsync(p => p.PaymentId == paymentId, cancellationToken);
    }

    public Task<Payment?> GetByTransferContentAsync(string transferContent, CancellationToken cancellationToken = default)
    {
        return _dbContext.Payments
            .Include(p => p.Booking)
            .SingleOrDefaultAsync(p => p.TransferContent == transferContent, cancellationToken);
    }

    public async Task AddPaymentAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        await _dbContext.Payments.AddAsync(payment, cancellationToken);
    }

    public async Task AddBookingAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        await _dbContext.Bookings.AddAsync(booking, cancellationToken);
    }

    public void RemoveBooking(Booking booking)
    {
        _dbContext.Bookings.Remove(booking);
    }

    public async Task AddSePayTransactionAsync(SePayTransaction transaction, CancellationToken cancellationToken = default)
    {
        await _dbContext.SePayTransactions.AddAsync(transaction, cancellationToken);
    }

    public async Task AddAuditLogAsync(VenueAuditLog log, CancellationToken cancellationToken = default)
    {
        await _dbContext.VenueAuditLogs.AddAsync(log, cancellationToken);
    }

    public async Task AddOwnerBankAccountAsync(OwnerBankAccount account, CancellationToken cancellationToken = default)
    {
        await _dbContext.OwnerBankAccounts.AddAsync(account, cancellationToken);
    }

    public async Task AddTicketSessionAsync(TicketSession session, CancellationToken cancellationToken = default)
    {
        await _dbContext.TicketSessions.AddAsync(session, cancellationToken);
    }

    public async Task AddSessionTicketAsync(SessionTicket ticket, CancellationToken cancellationToken = default)
    {
        await _dbContext.SessionTickets.AddAsync(ticket, cancellationToken);
    }

    public async Task<bool> IsListingFeeSchemaReadyAsync(CancellationToken cancellationToken = default)
    {
        var result = await _dbContext.Database.SqlQueryRaw<int>(
                """
                SELECT CASE
                    WHEN OBJECT_ID(N'[VENUE_LISTING_PAYMENT]', N'U') IS NULL THEN 0
                    ELSE 1
                END AS [Value]
                """)
            .SingleAsync(cancellationToken);

        return result == 1;
    }

    public async Task<IReadOnlyList<ExpiringVenueInfo>> GetExpiringListingFeeVenuesAsync(DateTime now, DateTime expiringThreshold, CancellationToken cancellationToken = default)
    {
        var latestPaidUntilByVenue = await _dbContext.VenueListingPayments.AsNoTracking()
            .Where(payment => payment.Status == "Confirmed" && payment.PaidUntil != null)
            .GroupBy(payment => payment.VenueId)
            .Select(group => new
            {
                VenueId = group.Key,
                PaidUntil = group.Max(payment => payment.PaidUntil)
            })
            .ToListAsync(cancellationToken);

        var expiringVenueIds = latestPaidUntilByVenue
            .Where(item => item.PaidUntil >= now && item.PaidUntil <= expiringThreshold)
            .Select(item => item.VenueId)
            .ToList();
        if (expiringVenueIds.Count == 0) return Array.Empty<ExpiringVenueInfo>();

        var paidUntilByVenue = latestPaidUntilByVenue.ToDictionary(item => item.VenueId, item => item.PaidUntil);
        var venues = await _dbContext.Venues.AsNoTracking()
            .Where(venue => expiringVenueIds.Contains(venue.VenueId))
            .Select(venue => new
            {
                venue.VenueId,
                venue.VenueName,
                OwnerUserId = venue.Owner.UserId
            })
            .ToListAsync(cancellationToken);

        return venues.Select(v => new ExpiringVenueInfo(v.VenueId, v.VenueName, v.OwnerUserId, paidUntilByVenue[v.VenueId])).ToList();
    }

    public Task<bool> HasSentListingFeeReminderTodayAsync(int userId, string linkTo, DateTime todayStart, CancellationToken cancellationToken = default)
    {
        return _dbContext.NotificationLogs.AsNoTracking()
            .AnyAsync(notification =>
                notification.UserId == userId
                && notification.Title == NotificationTitles.ListingFeeExpiring
                && notification.LinkTo == linkTo
                && notification.CreatedAt >= todayStart,
                cancellationToken);
    }

    public async Task<decimal> GetCurrentListingPriceAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ListingFeeSettings.AsNoTracking()
            .OrderByDescending(setting => setting.UpdatedAt)
            .ThenByDescending(setting => setting.ListingFeeSettingId)
            .Select(setting => setting.PricePerCourtPerMonth)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        return _dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
