using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Admin;

public sealed class AdminBookingQueryService
{
    private readonly ApplicationDbContext _dbContext;

    public AdminBookingQueryService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PaginatedResponse<AdminBookingSummaryResponse>> ListAsync(
        string? search,
        string? status,
        string? paymentStatus,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();
        var normalizedStatus = Normalize(status);
        var normalizedPaymentStatus = Normalize(paymentStatus);

        var query = _dbContext.Bookings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(booking =>
                (booking.BookingCode != null && booking.BookingCode.Contains(keyword))
                || booking.Court.Venue.VenueName.Contains(keyword)
                || booking.Court.Venue.Owner.User.Username.Contains(keyword)
                || booking.Court.Venue.Owner.User.Email.Contains(keyword)
                || (booking.Player != null && booking.Player.User.Username.Contains(keyword))
                || (booking.Player != null && booking.Player.User.Email.Contains(keyword)));
        }

        if (normalizedStatus is not null)
            query = query.Where(booking => booking.Status == normalizedStatus);

        if (normalizedPaymentStatus is not null)
            query = query.Where(booking => booking.Payments.Any(payment => payment.Status == normalizedPaymentStatus));

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(booking => booking.StartTime)
            .ThenByDescending(booking => booking.BookingId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(booking => new AdminBookingSummaryResponse
            {
                BookingId = booking.BookingId,
                BookingCode = booking.BookingCode,
                Status = booking.Status,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                CreatedAt = booking.CreatedAt,
                TotalAmount = booking.TotalAmount,
                CourtAmount = booking.CourtAmount,
                VenueId = booking.Court.VenueId,
                VenueName = booking.Court.Venue.VenueName,
                CourtId = booking.CourtId,
                CourtNumber = booking.Court.CourtNumber,
                OwnerName = booking.Court.Venue.Owner.User.Username,
                OwnerEmail = booking.Court.Venue.Owner.User.Email,
                PlayerName = booking.Player != null ? booking.Player.User.Username : "Owner tÃ¡ÂºÂ¡o lÃ¡Â»â€¹ch",
                PlayerEmail = booking.Player != null ? booking.Player.User.Email : null,
                PaymentStatus = booking.Payments
                    .OrderByDescending(payment => payment.SubmittedAt ?? payment.PaidAt ?? DateTime.MinValue)
                    .Select(payment => payment.Status)
                    .FirstOrDefault() ?? "NoPayment",
                PaymentMethod = booking.Payments
                    .OrderByDescending(payment => payment.SubmittedAt ?? payment.PaidAt ?? DateTime.MinValue)
                    .Select(payment => payment.PaymentMethod)
                    .FirstOrDefault(),
                PaymentSubmittedAt = booking.Payments
                    .OrderByDescending(payment => payment.SubmittedAt ?? payment.PaidAt ?? DateTime.MinValue)
                    .Select(payment => payment.SubmittedAt)
                    .FirstOrDefault(),
                PaymentVerifiedAt = booking.Payments
                    .OrderByDescending(payment => payment.VerifiedAt ?? payment.PaidAt ?? DateTime.MinValue)
                    .Select(payment => payment.VerifiedAt)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return Pagination.Create(items, totalCount, page, pageSize);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}