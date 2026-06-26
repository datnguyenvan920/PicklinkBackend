using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "VenueOwner")]
[Route("api/owner")]
public class OwnerOperationsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public OwnerOperationsController(ApplicationDbContext dbContext) => _dbContext = dbContext;

    [HttpGet("bookings")]
    public async Task<ActionResult<PaginatedResponse<OwnerBookingResponse>>> GetBookings(
        DateOnly? from,
        DateOnly? to,
        string? status,
        string? search,
        string? bookingType,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isMatchBooking = bookingType?.Equals("match", StringComparison.OrdinalIgnoreCase) == true;
        var query = _dbContext.Bookings
            .AsNoTracking()
            .Where(item => item.PlayerId != null && item.Court.Venue.Owner.UserId == userId.Value);
        if (bookingType?.Equals("regular", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(item => item.MatchId == null);
        else if (isMatchBooking)
            query = query.Where(item => item.MatchId != null);
        if (from.HasValue)
        {
            var start = from.Value.ToDateTime(TimeOnly.MinValue);
            query = isMatchBooking
                ? query.Where(item => item.CreatedAt >= start)
                : query.Where(item => item.StartTime >= start);
        }
        if (to.HasValue)
        {
            var end = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = isMatchBooking
                ? query.Where(item => item.CreatedAt < end)
                : query.Where(item => item.StartTime < end);
        }
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(item => item.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(item =>
                (item.BookingCode != null && item.BookingCode.Contains(keyword)) ||
                item.Player!.User.Username.Contains(keyword) ||
                item.Court.Venue.VenueName.Contains(keyword));
        }

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var localNow = DateTime.Now;
        var bookings = await (isMatchBooking
                ? query.OrderByDescending(item => item.CreatedAt)
                : query.OrderByDescending(item => item.StartTime))
            .ThenByDescending(item => item.BookingId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new OwnerBookingResponse
            {
                BookingId = item.BookingId,
                MatchId = item.MatchId,
                MatchType = item.Match == null ? null : item.Match.MatchType,
                RequiredPlayerCount = item.Match == null ? null : item.Match.RequiredPlayerCount,
                AcceptedPlayerCount = item.Match == null
                    ? null
                    : item.Match.MatchParticipants.Count(participant => participant.Status == "Approved" || participant.Status == "Accepted"),
                MatchPlayers = item.Match == null
                    ? new List<OwnerMatchPlayerResponse>()
                    : item.Match.MatchParticipants
                        .Where(participant => participant.Status == "Approved" || participant.Status == "Accepted")
                        .OrderByDescending(participant => participant.IsHost)
                        .ThenBy(participant => participant.RequestedAt)
                        .Select(participant => new OwnerMatchPlayerResponse
                        {
                            PlayerId = participant.PlayerId,
                            PlayerName = participant.Player.User.Username,
                            IsHost = participant.IsHost,
                            PaymentStatus = item.Payments
                                .Where(payment => payment.PayerId == participant.PlayerId)
                                .OrderByDescending(payment => payment.PaymentId)
                                .Select(payment => payment.Status)
                                .FirstOrDefault() ?? "Pending"
                        })
                        .ToList(),
                BookingCode = item.BookingCode ?? string.Empty,
                BookingStatus = item.Status,
                CheckInStatus = item.Status == "Cancelled" || item.Status == "Expired"
                    ? "Cancelled"
                    : item.Operation != null
                        ? item.Operation.CheckInStatus
                        : item.Status == "Confirmed" && localNow >= item.StartTime.AddMinutes(-30)
                            ? "Ready"
                            : "NotOpen",
                PaymentStatus = item.Payments
                    .OrderByDescending(payment => payment.Status == "WaitingForConfirmation")
                    .ThenByDescending(payment => payment.Status == "Pending")
                    .ThenByDescending(payment => payment.Status == "Paid")
                    .ThenByDescending(payment => payment.SubmittedAt)
                    .ThenByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.Status).FirstOrDefault() ?? "Pending",
                PaymentMethod = item.Payments
                    .OrderByDescending(payment => payment.Status == "WaitingForConfirmation")
                    .ThenByDescending(payment => payment.SubmittedAt)
                    .ThenByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.PaymentMethod).FirstOrDefault(),
                PaymentId = item.Payments
                    .OrderByDescending(payment => payment.Status == "WaitingForConfirmation")
                    .ThenByDescending(payment => payment.SubmittedAt)
                    .ThenByDescending(payment => payment.PaymentId)
                    .Select(payment => (int?)payment.PaymentId).FirstOrDefault(),
                TotalAmount = item.TotalAmount,
                CourtAmount = item.CourtAmount,
                HourlyPrice = item.HourlyPriceSnapshot,
                VenueId = item.Court.VenueId,
                VenueName = item.Court.Venue.VenueName,
                VenuePhone = item.Court.Venue.PhoneNumber,
                Address = item.Court.Venue.Address,
                CourtId = item.CourtId,
                CourtNumber = item.Court.CourtNumber,
                PlayerName = item.Player!.User.Username,
                PlayerEmail = item.Player.User.Email,
                PlayerCity = item.Player.User.City,
                PlayerCommune = item.Player.User.Commune,
                StartTime = item.StartTime,
                EndTime = item.EndTime,
                CreatedAt = item.CreatedAt,
                HoldExpiresAt = item.HoldExpiresAt,
                CodeVerifiedAt = item.Operation == null ? null : item.Operation.CodeVerifiedAt,
                PaymentConfirmedAt = item.Operation == null ? null : item.Operation.PaymentConfirmedAt,
                CheckedInAt = item.Operation == null ? null : item.Operation.CheckedInAt,
                NoShowAt = item.Operation == null ? null : item.Operation.NoShowAt,
                PaymentPaidAt = item.Payments.OrderByDescending(payment => payment.PaidAt)
                    .Select(payment => payment.PaidAt).FirstOrDefault(),
                PaymentVerifiedAt = item.Payments.OrderByDescending(payment => payment.VerifiedAt)
                    .Select(payment => payment.VerifiedAt).FirstOrDefault(),
                TransferCode = item.Payments
                    .OrderByDescending(payment => payment.Status == "WaitingForConfirmation")
                    .ThenByDescending(payment => payment.SubmittedAt)
                    .ThenByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.TransferCode).FirstOrDefault(),
                ReceiptImageUrl = item.Payments
                    .Where(payment => payment.ReceiptImageUrl != null)
                    .OrderByDescending(payment => payment.SubmittedAt)
                    .ThenByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.ReceiptImageUrl).FirstOrDefault(),
                RejectionReason = item.Payments
                    .Where(payment => payment.RejectionReason != null)
                    .OrderByDescending(payment => payment.VerifiedAt)
                    .ThenByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.RejectionReason).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
        foreach (var booking in bookings)
        {
            if (string.IsNullOrWhiteSpace(booking.BookingCode)) booking.BookingCode = $"PL-{booking.BookingId}";
            NormalizeBookingDates(booking);
        }
        return Ok(Pagination.Create(bookings, totalCount, page, pageSize));
    }

    [HttpGet("bookings/{bookingId:int}")]
    public async Task<ActionResult<OwnerBookingResponse>> GetBooking(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var booking = await BookingQuery(userId.Value).SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking thuộc cụm sân của Owner." });
        var actorIds = booking.StatusHistories.Select(item => item.ActorUserId)
            .Concat(booking.Payments.SelectMany(item => item.StatusHistories).Select(item => item.ActorUserId))
            .Concat(new[]
            {
                booking.Operation?.CodeVerifiedByUserId,
                booking.Operation?.PaymentConfirmedByUserId,
                booking.Operation?.CheckedInByUserId,
                booking.Operation?.NoShowByUserId
            })
            .Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToList();
        var actors = await _dbContext.Users.AsNoTracking().Where(item => actorIds.Contains(item.UserId))
            .ToDictionaryAsync(item => item.UserId, item => item.Username, cancellationToken);
        return Ok(MapBooking(booking, actors));
    }

    [HttpGet("reports/revenue")]
    public async Task<ActionResult<OwnerRevenueReportResponse>> GetRevenueReport(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
            return BadRequest(new { message = "Khoảng báo cáo phải từ 1 đến 367 ngày." });
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var bookings = await BookingQuery(userId.Value, includeHistory: false)
            .Where(item => item.StartTime >= start && item.StartTime < end)
            .OrderBy(item => item.StartTime)
            .ToListAsync(cancellationToken);
        var records = bookings.Select(item => MapBooking(item)).ToList();
        var paid = records.Where(item => item.PaymentStatus == "Paid" && item.BookingStatus != "Cancelled").ToList();
        return Ok(new OwnerRevenueReportResponse
        {
            From = from,
            To = to,
            GrossRevenue = paid.Sum(item => item.TotalAmount),
            PaidBookings = paid.Count,
            PendingAmount = records.Where(item => item.PaymentStatus is "Pending" or "WaitingForConfirmation").Sum(item => item.TotalAmount),
            CancelledBookings = records.Count(item => item.BookingStatus is "Cancelled" or "Expired"),
            AverageBookingValue = paid.Count == 0 ? 0 : paid.Average(item => item.TotalAmount),
            Daily = paid.GroupBy(item => DateOnly.FromDateTime(item.StartTime)).Select(group => new OwnerDailyRevenueResponse
            {
                Date = group.Key, Revenue = group.Sum(item => item.TotalAmount), BookingCount = group.Count()
            }).OrderBy(item => item.Date).ToList(),
            Bookings = records
        });
    }

    private IQueryable<Booking> BookingQuery(int userId, bool includeHistory = true)
    {
        IQueryable<Booking> query = _dbContext.Bookings.AsNoTracking()
            .AsSplitQuery()
            .Where(item => item.PlayerId != null && item.Court.Venue.Owner.UserId == userId)
            .Include(item => item.Operation)
            .Include(item => item.Payments)
            .Include(item => item.Player).ThenInclude(item => item!.User)
            .Include(item => item.Match).ThenInclude(item => item!.MatchParticipants)
                .ThenInclude(item => item.Player).ThenInclude(item => item.User)
            .Include(item => item.Court).ThenInclude(item => item.Venue);

        if (includeHistory)
        {
            query = query
                .Include(item => item.StatusHistories)
                .Include(item => item.Payments).ThenInclude(item => item.StatusHistories);
        }

        return query;
    }

    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private static OwnerBookingResponse MapBooking(Booking booking, IReadOnlyDictionary<int, string>? actors = null)
    {
        var payment = booking.Payments
            .OrderByDescending(item => item.Status == "WaitingForConfirmation")
            .ThenByDescending(item => item.Status == "Pending")
            .ThenByDescending(item => item.Status == "Paid")
            .ThenByDescending(item => item.SubmittedAt)
            .ThenByDescending(item => item.PaymentId)
            .FirstOrDefault();
        var localNow = DateTime.Now;
        var checkInStatus = booking.Status is "Cancelled" or "Expired"
            ? "Cancelled"
            : booking.Operation?.CheckInStatus ?? (booking.Status == "Confirmed" && localNow >= booking.StartTime.AddMinutes(-30) ? "Ready" : "NotOpen");
        return new OwnerBookingResponse
        {
            BookingId = booking.BookingId,
            MatchId = booking.MatchId,
            MatchType = booking.Match?.MatchType,
            RequiredPlayerCount = booking.Match?.RequiredPlayerCount,
            AcceptedPlayerCount = booking.Match?.MatchParticipants.Count(item => item.Status == "Approved" || item.Status == "Accepted"),
            MatchPlayers = booking.Match?.MatchParticipants
                .Where(item => item.Status == "Approved" || item.Status == "Accepted")
                .OrderByDescending(item => item.IsHost)
                .ThenBy(item => item.RequestedAt)
                .Select(item => new OwnerMatchPlayerResponse
                {
                    PlayerId = item.PlayerId,
                    PlayerName = item.Player.User.Username,
                    IsHost = item.IsHost,
                    PaymentStatus = booking.Payments
                        .Where(paymentItem => paymentItem.PayerId == item.PlayerId)
                        .OrderByDescending(paymentItem => paymentItem.PaymentId)
                        .Select(paymentItem => paymentItem.Status)
                        .FirstOrDefault() ?? "Pending"
                })
                .ToList() ?? new List<OwnerMatchPlayerResponse>(),
            BookingCode = booking.BookingCode ?? $"PL-{booking.BookingId}",
            BookingStatus = booking.Status,
            CheckInStatus = checkInStatus,
            PaymentStatus = payment?.Status ?? "Pending",
            PaymentMethod = payment?.PaymentMethod,
            PaymentId = payment?.PaymentId,
            TotalAmount = booking.TotalAmount,
            CourtAmount = booking.CourtAmount,
            HourlyPrice = booking.HourlyPriceSnapshot,
            VenueId = booking.Court.VenueId,
            VenueName = booking.Court.Venue.VenueName,
            VenuePhone = booking.Court.Venue.PhoneNumber,
            Address = booking.Court.Venue.Address,
            CourtId = booking.CourtId,
            CourtNumber = booking.Court.CourtNumber,
            PlayerName = booking.Player?.User.Username ?? "Khách",
            PlayerEmail = booking.Player?.User.Email,
            PlayerCity = booking.Player?.User.City,
            PlayerCommune = booking.Player?.User.Commune,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            CreatedAt = AsUtc(booking.CreatedAt),
            HoldExpiresAt = AsUtc(booking.HoldExpiresAt),
            CodeVerifiedAt = AsUtc(booking.Operation?.CodeVerifiedAt),
            PaymentConfirmedAt = AsUtc(booking.Operation?.PaymentConfirmedAt),
            CheckedInAt = AsUtc(booking.Operation?.CheckedInAt),
            NoShowAt = AsUtc(booking.Operation?.NoShowAt),
            CodeVerifiedBy = ActorName(booking.Operation?.CodeVerifiedByUserId, actors),
            PaymentConfirmedBy = ActorName(booking.Operation?.PaymentConfirmedByUserId, actors),
            CheckedInBy = ActorName(booking.Operation?.CheckedInByUserId, actors),
            NoShowBy = ActorName(booking.Operation?.NoShowByUserId, actors),
            PaymentPaidAt = AsUtc(payment?.PaidAt),
            PaymentVerifiedAt = AsUtc(payment?.VerifiedAt),
            TransferCode = payment?.TransferCode,
            ReceiptImageUrl = payment?.ReceiptImageUrl,
            RejectionReason = payment?.RejectionReason,
            BookingHistory = booking.StatusHistories.OrderBy(item => item.ChangedAt).Select(item => new OwnerBookingHistoryResponse
            {
                FromStatus = item.FromStatus,
                ToStatus = item.ToStatus,
                Reason = item.Reason,
                ActorName = ActorName(item.ActorUserId, actors),
                ChangedAt = AsUtc(item.ChangedAt)
            }).ToList(),
            PaymentHistory = payment?.StatusHistories.OrderBy(item => item.CreatedAt).Select(item => new OwnerPaymentHistoryResponse
            {
                FromStatus = item.FromStatus,
                ToStatus = item.ToStatus,
                Action = item.Action,
                Reason = item.Reason,
                ActorName = ActorName(item.ActorUserId, actors),
                CreatedAt = AsUtc(item.CreatedAt)
            }).ToList() ?? new List<OwnerPaymentHistoryResponse>()
        };
    }

    private static string? ActorName(int? actorId, IReadOnlyDictionary<int, string>? actors) =>
        actorId.HasValue && actors is not null && actors.TryGetValue(actorId.Value, out var name) ? name : null;
    private static void NormalizeBookingDates(OwnerBookingResponse booking)
    {
        booking.CreatedAt = AsUtc(booking.CreatedAt);
        booking.HoldExpiresAt = AsUtc(booking.HoldExpiresAt);
        booking.CodeVerifiedAt = AsUtc(booking.CodeVerifiedAt);
        booking.PaymentConfirmedAt = AsUtc(booking.PaymentConfirmedAt);
        booking.CheckedInAt = AsUtc(booking.CheckedInAt);
        booking.NoShowAt = AsUtc(booking.NoShowAt);
        booking.PaymentPaidAt = AsUtc(booking.PaymentPaidAt);
        booking.PaymentVerifiedAt = AsUtc(booking.PaymentVerifiedAt);
    }
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}

public class OwnerBookingResponse
{
    public int BookingId { get; set; }
    public int? MatchId { get; set; }
    public string? MatchType { get; set; }
    public int? RequiredPlayerCount { get; set; }
    public int? AcceptedPlayerCount { get; set; }
    public List<OwnerMatchPlayerResponse> MatchPlayers { get; set; } = new();
    public string BookingCode { get; set; } = string.Empty;
    public string BookingStatus { get; set; } = string.Empty;
    public string CheckInStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public int? PaymentId { get; set; }
    public double TotalAmount { get; set; }
    public double CourtAmount { get; set; }
    public double HourlyPrice { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string? VenuePhone { get; set; }
    public string Address { get; set; } = string.Empty;
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string? PlayerEmail { get; set; }
    public string? PlayerCity { get; set; }
    public string? PlayerCommune { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
    public DateTime? CodeVerifiedAt { get; set; }
    public DateTime? PaymentConfirmedAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? NoShowAt { get; set; }
    public string? CodeVerifiedBy { get; set; }
    public string? PaymentConfirmedBy { get; set; }
    public string? CheckedInBy { get; set; }
    public string? NoShowBy { get; set; }
    public DateTime? PaymentPaidAt { get; set; }
    public DateTime? PaymentVerifiedAt { get; set; }
    public string? TransferCode { get; set; }
    public string? ReceiptImageUrl { get; set; }
    public string? RejectionReason { get; set; }
    public List<OwnerBookingHistoryResponse> BookingHistory { get; set; } = new();
    public List<OwnerPaymentHistoryResponse> PaymentHistory { get; set; } = new();
}
public class OwnerMatchPlayerResponse
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}
public class OwnerBookingHistoryResponse
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? ActorName { get; set; }
    public DateTime ChangedAt { get; set; }
}
public class OwnerPaymentHistoryResponse
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? ActorName { get; set; }
    public DateTime CreatedAt { get; set; }
}
public class OwnerRevenueReportResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public double GrossRevenue { get; set; }
    public int PaidBookings { get; set; }
    public double PendingAmount { get; set; }
    public int CancelledBookings { get; set; }
    public double AverageBookingValue { get; set; }
    public List<OwnerDailyRevenueResponse> Daily { get; set; } = new();
    public List<OwnerBookingResponse> Bookings { get; set; } = new();
}
public class OwnerDailyRevenueResponse
{
    public DateOnly Date { get; set; }
    public double Revenue { get; set; }
    public int BookingCount { get; set; }
}
