using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/dashboard")]
public class AdminDashboardController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminDashboardController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<AdminDashboardResponse>> GetDashboard(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var tomorrowStart = todayStart.AddDays(1);
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var expiringThreshold = now.AddDays(7);

        var totalUsers = await _dbContext.Users.AsNoTracking().CountAsync(cancellationToken);
        var lockedUserCount = await _dbContext.Users.AsNoTracking()
            .CountAsync(user => user.IsLocked, cancellationToken);
        var activeVenueCount = await _dbContext.Venues.AsNoTracking()
            .CountAsync(venue => venue.ApprovalStatus == "Approved" && venue.IsOpen, cancellationToken);
        var pendingVenueCount = await _dbContext.Venues.AsNoTracking()
            .CountAsync(venue => venue.ApprovalStatus == "Pending", cancellationToken);
        var totalCourtCount = await _dbContext.Courts.AsNoTracking()
            .CountAsync(court => court.AvailabilityStatus != "Inactive", cancellationToken);
        var todayBookingCount = await _dbContext.Bookings.AsNoTracking()
            .CountAsync(booking => booking.CreatedAt >= todayStart && booking.CreatedAt < tomorrowStart, cancellationToken);
        var todayBookingRevenue = await _dbContext.Payments.AsNoTracking()
            .Where(payment => payment.Status == "Verified"
                && payment.PaidAt != null
                && payment.PaidAt >= todayStart
                && payment.PaidAt < tomorrowStart)
            .SumAsync(payment => (double?)payment.Amount, cancellationToken) ?? 0;
        var pendingBookingPaymentCount = await _dbContext.Payments.AsNoTracking()
            .CountAsync(payment => payment.Status == "WaitingForConfirmation", cancellationToken);
        var pendingListingPaymentCount = await _dbContext.VenueListingPayments.AsNoTracking()
            .CountAsync(payment => payment.Status == "PendingReview", cancellationToken);
        var listingRevenueThisMonth = await _dbContext.VenueListingPayments.AsNoTracking()
            .Where(payment => payment.Status == "Confirmed"
                && payment.ReviewedAt != null
                && payment.ReviewedAt >= monthStart
                && payment.ReviewedAt < nextMonthStart)
            .SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0;

        var latestListingExpirations = await _dbContext.VenueListingPayments.AsNoTracking()
            .Where(payment => payment.Status == "Confirmed" && payment.PaidUntil != null)
            .GroupBy(payment => payment.VenueId)
            .Select(group => new
            {
                VenueId = group.Key,
                PaidUntil = group.Max(payment => payment.PaidUntil)
            })
            .ToListAsync(cancellationToken);

        var expiringIds = latestListingExpirations
            .Where(item => item.PaidUntil >= now && item.PaidUntil <= expiringThreshold)
            .Select(item => item.VenueId)
            .ToList();
        var expiredIds = latestListingExpirations
            .Where(item => item.PaidUntil < now)
            .Select(item => item.VenueId)
            .ToList();

        var expiringListingCount = expiringIds.Count;
        var expiredListingCount = expiredIds.Count;
        var paidUntilByVenue = latestListingExpirations.ToDictionary(item => item.VenueId, item => item.PaidUntil);

        var expiringListings = new List<AdminDashboardExpiringListingResponse>();
        if (expiringIds.Count > 0)
        {
            expiringListings = await _dbContext.Venues.AsNoTracking()
                .Where(venue => expiringIds.Contains(venue.VenueId))
                .OrderBy(venue => venue.VenueName)
                .Take(8)
                .Select(venue => new AdminDashboardExpiringListingResponse
                {
                    VenueId = venue.VenueId,
                    VenueName = venue.VenueName,
                    OwnerName = venue.Owner.User.Username,
                    OwnerEmail = venue.Owner.User.Email,
                    CourtCount = venue.Courts.Count(court => court.AvailabilityStatus != "Inactive"),
                })
                .ToListAsync(cancellationToken);

            foreach (var listing in expiringListings)
            {
                listing.PaidUntil = paidUntilByVenue[listing.VenueId];
            }
        }

        var actionItems = await BuildActionItems(now, cancellationToken);
        actionItems.AddRange(expiringListings.Take(3).Select(item => new AdminDashboardActionItemResponse
        {
            Type = "ListingExpiring",
            Title = item.VenueName,
            Description = $"{item.OwnerName} - het han phi len san ngay {item.PaidUntil:dd/MM/yyyy}.",
            Status = "Sap het han",
            Tone = "warning",
            LinkTo = "/admin/transactions",
            CreatedAt = item.PaidUntil
        }));

        return Ok(new AdminDashboardResponse
        {
            TotalUsers = totalUsers,
            LockedUserCount = lockedUserCount,
            ActiveVenueCount = activeVenueCount,
            PendingVenueCount = pendingVenueCount,
            TotalCourtCount = totalCourtCount,
            TodayBookingCount = todayBookingCount,
            TodayBookingRevenue = todayBookingRevenue,
            PendingBookingPaymentCount = pendingBookingPaymentCount,
            PendingListingPaymentCount = pendingListingPaymentCount,
            ListingRevenueThisMonth = listingRevenueThisMonth,
            ExpiringListingCount = expiringListingCount,
            ExpiredListingCount = expiredListingCount,
            ActionItems = actionItems
                .OrderByDescending(item => item.Tone == "danger")
                .ThenByDescending(item => item.Tone == "warning")
                .ThenByDescending(item => item.CreatedAt)
                .Take(12)
                .ToList(),
            ExpiringListings = expiringListings
        });
    }

    private async Task<List<AdminDashboardActionItemResponse>> BuildActionItems(
        DateTime now,
        CancellationToken cancellationToken)
    {
        var submittedVenues = await _dbContext.Venues.AsNoTracking()
            .Where(venue => venue.ApprovalStatus == "Pending")
            .OrderByDescending(venue => venue.VenueAuditLogs
                .Where(log => log.Action == "OwnerSubmittedForApproval")
                .Select(log => (DateTime?)log.Timestamp)
                .Max())
            .Take(4)
            .Select(venue => new AdminDashboardActionItemResponse
            {
                Type = "VenueApproval",
                Title = venue.VenueName,
                Description = $"{venue.Owner.User.Username} - {venue.Courts.Count} san con dang cho duyet.",
                Status = "Cho duyet san",
                Tone = "warning",
                LinkTo = "/admin/courts",
                CreatedAt = venue.VenueAuditLogs
                    .Where(log => log.Action == "OwnerSubmittedForApproval")
                    .Select(log => (DateTime?)log.Timestamp)
                    .Max()
            })
            .ToListAsync(cancellationToken);

        var listingPayments = await _dbContext.VenueListingPayments.AsNoTracking()
            .Where(payment => payment.Status == "PendingReview")
            .OrderByDescending(payment => payment.SubmittedAt)
            .Take(4)
            .Select(payment => new AdminDashboardActionItemResponse
            {
                Type = "ListingPayment",
                Title = payment.Venue.VenueName,
                Description = $"{payment.Venue.Owner.User.Username} - bien lai phi len san {payment.Amount:n0} VND.",
                Status = "Cho duyet bien lai",
                Tone = "info",
                LinkTo = "/admin/transactions",
                CreatedAt = payment.SubmittedAt
            })
            .ToListAsync(cancellationToken);

        var stalePayments = await _dbContext.Payments.AsNoTracking()
            .Where(payment => payment.Status == "WaitingForConfirmation"
                && payment.SubmittedAt != null
                && payment.SubmittedAt <= now.AddHours(-24))
            .OrderBy(payment => payment.SubmittedAt)
            .Take(4)
            .Select(payment => new AdminDashboardActionItemResponse
            {
                Type = "BookingPayment",
                Title = payment.Booking.BookingCode ?? $"Booking #{payment.BookingId}",
                Description = $"{payment.Payer.User.Username} - bien lai booking qua 24 gio.",
                Status = "Can ho tro",
                Tone = "danger",
                LinkTo = "/admin/bookings",
                CreatedAt = payment.SubmittedAt
            })
            .ToListAsync(cancellationToken);

        return submittedVenues.Concat(listingPayments).Concat(stalePayments).ToList();
    }
}

public sealed class AdminDashboardResponse
{
    public int TotalUsers { get; set; }
    public int LockedUserCount { get; set; }
    public int ActiveVenueCount { get; set; }
    public int PendingVenueCount { get; set; }
    public int TotalCourtCount { get; set; }
    public int TodayBookingCount { get; set; }
    public double TodayBookingRevenue { get; set; }
    public int PendingBookingPaymentCount { get; set; }
    public int PendingListingPaymentCount { get; set; }
    public decimal ListingRevenueThisMonth { get; set; }
    public int ExpiringListingCount { get; set; }
    public int ExpiredListingCount { get; set; }
    public List<AdminDashboardActionItemResponse> ActionItems { get; set; } = [];
    public List<AdminDashboardExpiringListingResponse> ExpiringListings { get; set; } = [];
}

public sealed class AdminDashboardActionItemResponse
{
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Tone { get; set; } = "neutral";
    public string LinkTo { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}

public sealed class AdminDashboardExpiringListingResponse
{
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public int CourtCount { get; set; }
    public DateTime? PaidUntil { get; set; }
}
