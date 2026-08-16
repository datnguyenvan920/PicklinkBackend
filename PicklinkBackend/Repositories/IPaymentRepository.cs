using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using PicklinkBackend.Models;

namespace PicklinkBackend.Repositories;

public sealed record ExpiringVenueInfo(int VenueId, string VenueName, int OwnerUserId, DateTime? PaidUntil);

public interface IPaymentRepository
{
    IQueryable<Payment> Payments { get; }
    IQueryable<SePayTransaction> SePayTransactions { get; }
    IQueryable<Booking> Bookings { get; }
    IQueryable<TicketSession> TicketSessions { get; }
    IQueryable<SessionTicket> SessionTickets { get; }
    IQueryable<Match> Matches { get; }
    IQueryable<BookingSlot> BookingSlots { get; }
    IQueryable<VenueAuditLog> VenueAuditLogs { get; }
    IQueryable<OwnerBankAccount> OwnerBankAccounts { get; }
    IQueryable<Player> Players { get; }
    IQueryable<Venue> Venues { get; }
    IQueryable<Staff> Staff { get; }

    Task<Payment?> GetByIdAsync(int paymentId, CancellationToken cancellationToken = default);
    Task<Payment?> GetByTransferContentAsync(string transferContent, CancellationToken cancellationToken = default);
    Task AddPaymentAsync(Payment payment, CancellationToken cancellationToken = default);
    Task AddBookingAsync(Booking booking, CancellationToken cancellationToken = default);
    void RemoveBooking(Booking booking);
    Task AddSePayTransactionAsync(SePayTransaction transaction, CancellationToken cancellationToken = default);
    Task AddAuditLogAsync(VenueAuditLog log, CancellationToken cancellationToken = default);
    Task AddOwnerBankAccountAsync(OwnerBankAccount account, CancellationToken cancellationToken = default);
    Task AddTicketSessionAsync(TicketSession session, CancellationToken cancellationToken = default);
    Task AddSessionTicketAsync(SessionTicket ticket, CancellationToken cancellationToken = default);
    Task<bool> IsListingFeeSchemaReadyAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExpiringVenueInfo>> GetExpiringListingFeeVenuesAsync(DateTime now, DateTime expiringThreshold, CancellationToken cancellationToken = default);
    Task<bool> HasSentListingFeeReminderTodayAsync(int userId, string linkTo, DateTime todayStart, CancellationToken cancellationToken = default);
    Task<decimal> GetCurrentListingPriceAsync(CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
