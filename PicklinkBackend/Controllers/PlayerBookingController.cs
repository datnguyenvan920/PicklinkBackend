using System.Data;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/player-bookings")]
public class PlayerBookingController : ControllerBase
{
    private static readonly string[] InactiveStatuses = ["Cancelled", "Expired"];
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;

    public PlayerBookingController(ApplicationDbContext dbContext, IConfiguration configuration, ScheduleRealtimeNotifier scheduleRealtime)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _scheduleRealtime = scheduleRealtime;
    }

    [AllowAnonymous]
    [HttpGet("venues")]
    public async Task<ActionResult<List<PlayerVenueSummaryResponse>>> GetVenues(CancellationToken cancellationToken)
    {
        var venues = await _dbContext.Venues.AsNoTracking()
            .Where(venue => venue.IsOpen && venue.Courts.Any(court => court.AvailabilityStatus == "Available"))
            .Include(venue => venue.Courts)
            .Include(venue => venue.BookingRules)
            .Include(venue => venue.VenueImages)
            .OrderBy(venue => venue.VenueName)
            .ToListAsync(cancellationToken);

        return Ok(venues.Select(venue => new PlayerVenueSummaryResponse
        {
            VenueId = venue.VenueId,
            VenueName = venue.VenueName,
            Address = venue.Address,
            Latitude = venue.Latitude,
            Longitude = venue.Longitude,
            OverallRating = venue.OverallRating,
            OpenTime = venue.OpenTime.ToString("HH:mm"),
            CloseTime = venue.CloseTime.ToString("HH:mm"),
            ImageUrl = venue.VenueImages.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.SortOrder).Select(image => image.ImageUrl).FirstOrDefault(),
            FromPrice = venue.Courts.Where(court => court.AvailabilityStatus == "Available").Select(court => court.HourlyPrice).Where(price => price > 0).DefaultIfEmpty(GetBasePrice(venue)).Min(),
            CourtCount = venue.Courts.Count(court => court.AvailabilityStatus == "Available")
        }).ToList());
    }

    [AllowAnonymous]
    [HttpGet("venues/{venueId:int}/availability")]
    public async Task<ActionResult<PlayerCourtAvailabilityResponse>> GetAvailability(
        int venueId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var venue = await _dbContext.Venues.AsNoTracking()
            .Include(item => item.Courts)
            .Include(item => item.BookingRules)
            .SingleOrDefaultAsync(item => item.VenueId == venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var now = DateTime.UtcNow;
        var currentUserId = CurrentUserId();
        var bookings = await _dbContext.Bookings.AsNoTracking()
            .Where(booking => booking.Court.VenueId == venueId && booking.StartTime < dayEnd && booking.EndTime > dayStart &&
                !InactiveStatuses.Contains(booking.Status) && (booking.Status != "Holding" || booking.HoldExpiresAt > now))
            .Include(booking => booking.Player)
            .ToListAsync(cancellationToken);

        var response = new PlayerCourtAvailabilityResponse
        {
            VenueId = venue.VenueId,
            VenueName = venue.VenueName,
            Address = venue.Address,
            OpenTime = venue.OpenTime.ToString("HH:mm"),
            CloseTime = venue.CloseTime.ToString("HH:mm"),
            Date = date,
            Courts = venue.Courts.Where(court => court.AvailabilityStatus != "Inactive").OrderBy(court => court.CourtNumber).Select(court => new PlayerCourtResponse
            {
                CourtId = court.CourtId,
                CourtNumber = court.CourtNumber,
                CourtType = court.CourtType ?? "Tiêu chuẩn",
                SurfaceType = court.SurfaceType,
                IsIndoor = court.IsIndoor,
                HourlyPrice = court.HourlyPrice > 0 ? court.HourlyPrice : GetBasePrice(venue)
            }).ToList()
        };

        foreach (var court in venue.Courts.Where(item => item.AvailabilityStatus != "Inactive"))
        {
            var opening = date.ToDateTime(venue.OpenTime);
            var closing = date.ToDateTime(venue.CloseTime);
            for (var start = opening; start.AddMinutes(30) <= closing; start = start.AddMinutes(30))
            {
                var end = start.AddMinutes(30);
                var overlap = bookings.FirstOrDefault(booking => booking.CourtId == court.CourtId && booking.StartTime < end && booking.EndTime > start);
                var status = !venue.IsOpen ? "Closed"
                    : court.AvailabilityStatus == "Maintenance" ? "Maintenance"
                    : overlap is null ? "Available"
                    : overlap.Status == "Holding" ? "Holding"
                    : overlap.PlayerId is not null ? "Booked"
                    : overlap.OwnerEntryType ?? "Blocked";
                var isOwnedHolding = overlap?.Status == "Holding"
                    && currentUserId.HasValue
                    && overlap.Player?.UserId == currentUserId.Value;
                response.Slots.Add(new PlayerAvailabilitySlotResponse
                {
                    CourtId = court.CourtId,
                    StartTime = start,
                    EndTime = end,
                    Status = status,
                    BookingId = isOwnedHolding ? overlap!.BookingId : null,
                    IsOwnedByCurrentUser = isOwnedHolding
                });
            }
        }

        return Ok(response);
    }

    [Authorize]
    [HttpPost("hold")]
    public async Task<ActionResult<BookingHoldingResponse>> CreateHolding(
        CreateBookingHoldRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var player = await GetOrCreatePlayerAsync(userId.Value, cancellationToken);
        if (player is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        var selectedSlots = request.SlotStarts.Distinct().OrderBy(item => item).ToList();
        if (selectedSlots.Count != request.SlotStarts.Count)
            return BadRequest(new { message = "Danh sách slot bị trùng." });
        for (var index = 1; index < selectedSlots.Count; index++)
            if (selectedSlots[index].ToTimeSpan() - selectedSlots[index - 1].ToTimeSpan() != TimeSpan.FromMinutes(30))
                return BadRequest(new { message = "Chỉ được chọn các slot 30 phút liên tiếp." });
        if (selectedSlots.Any(slot => slot.Minute % 30 != 0 || slot.Second != 0))
            return BadRequest(new { message = "Slot phải bắt đầu tại phút 00 hoặc 30." });

        var startTime = request.Date.ToDateTime(selectedSlots[0]);
        var endTime = request.Date.ToDateTime(selectedSlots[^1]).AddMinutes(30);
        if (startTime <= DateTime.Now)
            return BadRequest(new { message = "Không thể giữ chỗ cho khung giờ đã qua." });

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"court-booking:{request.CourtId}", cancellationToken))
            return Conflict(new { message = "Sân đang được người khác thao tác. Vui lòng thử lại." });

        var court = await _dbContext.Courts
            .Include(item => item.Venue).ThenInclude(venue => venue.BookingRules)
            .SingleOrDefaultAsync(item => item.CourtId == request.CourtId, cancellationToken);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân con." });
        if (!court.Venue.IsOpen || court.AvailabilityStatus != "Available")
            return Conflict(new { message = "Sân hiện không nhận đặt chỗ." });

        var opening = request.Date.ToDateTime(court.Venue.OpenTime);
        var closing = request.Date.ToDateTime(court.Venue.CloseTime);
        if (startTime < opening || endTime > closing)
            return BadRequest(new { message = $"Khung giờ phải nằm trong giờ mở cửa {court.Venue.OpenTime:HH:mm}–{court.Venue.CloseTime:HH:mm}." });

        var utcNow = DateTime.UtcNow;
        var staleHoldings = await _dbContext.Bookings
            .Include(booking => booking.Payments).ThenInclude(payment => payment.StatusHistories)
            .Where(booking => booking.CourtId == request.CourtId && booking.Status == "Holding" && booking.HoldExpiresAt <= utcNow)
            .ToListAsync(cancellationToken);
        foreach (var stale in staleHoldings) await ExpireHoldingAsync(stale, "Hết 15 phút giữ chỗ", cancellationToken);
        if (staleHoldings.Count > 0) await _dbContext.SaveChangesAsync(cancellationToken);

        var overlaps = await _dbContext.Bookings.AnyAsync(booking =>
            booking.CourtId == request.CourtId && !InactiveStatuses.Contains(booking.Status) &&
            (booking.Status != "Holding" || booking.HoldExpiresAt > utcNow) &&
            booking.StartTime < endTime && booking.EndTime > startTime,
            cancellationToken);
        if (overlaps) return Conflict(new { message = "Một hoặc nhiều slot vừa được người khác giữ. Hãy tải lại lịch." });

        var hourlyPrice = court.HourlyPrice > 0 ? court.HourlyPrice : GetBasePrice(court.Venue);
        var durationHours = (endTime - startTime).TotalHours;
        var courtAmount = RoundMoney(hourlyPrice * durationHours);
        var total = courtAmount;
        var holdMinutes = Math.Clamp(_configuration.GetValue("Booking:HoldingMinutes", 15), 1, 60);

        var booking = new Booking
        {
            PlayerId = player.PlayerId,
            CourtId = court.CourtId,
            StartTime = startTime,
            EndTime = endTime,
            Status = "Holding",
            BookingCode = $"PL-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            CreatedAt = utcNow,
            HoldExpiresAt = utcNow.AddMinutes(holdMinutes),
            HourlyPriceSnapshot = hourlyPrice,
            CourtAmount = courtAmount,
            TotalAmount = total
        };
        booking.StatusHistories.Add(NewHistory(null, "Holding", "Player tạo giữ chỗ", userId));
        var bankAccount = await _dbContext.OwnerBankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == court.Venue.OwnerId && item.IsActive, cancellationToken);
        var transferContent = booking.BookingCode!;
        var payment = new Payment
        {
            PayerId = player.PlayerId,
            Amount = total,
            PaymentMethod = "BankTransfer",
            Status = "Pending",
            TransferCode = booking.BookingCode!.Replace("-", string.Empty),
            TransferContent = transferContent,
            BankCode = bankAccount?.BankCode,
            BankName = bankAccount?.BankName,
            BankAccountNumber = bankAccount?.AccountNumber,
            BankAccountName = bankAccount?.AccountHolderName,
            QrImageUrl = bankAccount is null ? null : BuildVietQrUrl(bankAccount, total, transferContent)
        };
        payment.StatusHistories.Add(NewPaymentHistory(null, "Pending", "Created", "Tạo yêu cầu chuyển khoản", userId));
        booking.Payments.Add(payment);
        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _scheduleRealtime.Publish(new ScheduleChangedEvent(court.VenueId, court.CourtId, booking.StartTime, booking.EndTime, "Holding", "Created"));

        return Ok(MapBooking(booking, court));
    }

    [Authorize]
    [HttpGet("{bookingId:int}")]
    public async Task<ActionResult<BookingHoldingResponse>> GetBooking(int bookingId, CancellationToken cancellationToken)
    {
        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        return booking is null ? NotFound(new { message = "Không tìm thấy booking." }) : Ok(MapBooking(booking, booking.Court));
    }

    [Authorize]
    [HttpPost("{bookingId:int}/pay")]
    public async Task<ActionResult<BookingHoldingResponse>> CompletePayment(
        int bookingId,
        CompleteBookingPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking." });
        if (booking.Status == "Confirmed") return Ok(MapBooking(booking, booking.Court));
        if (booking.Status != "Holding") return Conflict(new { message = $"Booking đang ở trạng thái {booking.Status}." });
        if (booking.HoldExpiresAt <= DateTime.UtcNow)
        {
            await ExpireHoldingAsync(booking, "Hết thời gian trước khi thanh toán", cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            PublishBookingChanged(booking, "Expired", "Deleted");
            return Conflict(new { message = "Thời gian giữ chỗ đã hết. Slot đã được mở lại." });
        }

        if (request.PaymentMethod == "BankTransfer")
            return Conflict(new { message = "Chuyển khoản ngân hàng cần gửi biên lai và chờ chủ sân xác nhận." });

        var previous = booking.Status;
        booking.Status = "Confirmed";
        booking.HoldExpiresAt = null;
        var payment = booking.Payments.OrderByDescending(item => item.PaymentId).First();
        var previousPaymentStatus = payment.Status;
        payment.PaymentMethod = request.PaymentMethod;
        if (request.PaymentMethod == "AtCourt")
        {
            payment.Status = "Pending";
            payment.PaidAt = null;
            payment.StatusHistories.Add(NewPaymentHistory(previousPaymentStatus, "Pending", "AtCourtSelected", "Khách chọn thanh toán tại sân", userId));
            booking.StatusHistories.Add(NewHistory(previous, "Confirmed", "Giữ sân - chờ thanh toán tại quầy", userId));
        }
        else
        {
            payment.Status = "Paid";
            payment.PaidAt = DateTime.UtcNow;
            payment.StatusHistories.Add(NewPaymentHistory(previousPaymentStatus, "Paid", "LegacyPaymentCompleted", $"Thanh toán {request.PaymentMethod}", userId));
            booking.StatusHistories.Add(NewHistory(previous, "Confirmed", $"Thanh toán {request.PaymentMethod} thành công", userId));
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishBookingChanged(booking, "Confirmed", "Updated");
        return Ok(MapBooking(booking, booking.Court));
    }

    [Authorize]
    [HttpDelete("{bookingId:int}/hold")]
    public async Task<ActionResult> CancelHolding(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý." });
        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking." });
        if (booking.Status != "Holding") return Conflict(new { message = "Chỉ có thể hủy booking đang giữ chỗ." });
        booking.Status = "Cancelled";
        booking.HoldExpiresAt = null;
        foreach (var payment in booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation"))
        {
            var fromPaymentStatus = payment.Status;
            payment.Status = "Cancelled";
            payment.StatusHistories.Add(NewPaymentHistory(fromPaymentStatus, "Cancelled", "BookingCancelled", "Player hủy giữ chỗ", userId));
        }
        booking.StatusHistories.Add(NewHistory("Holding", "Cancelled", "Player hủy giữ chỗ", userId));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishBookingChanged(booking, "Cancelled", "Deleted");
        return NoContent();
    }

    private async Task<Booking?> LoadOwnedBookingAsync(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        return await _dbContext.Bookings
            .Include(booking => booking.Court).ThenInclude(court => court.Venue)
            .Include(booking => booking.Player)
            .Include(booking => booking.Payments).ThenInclude(payment => payment.StatusHistories)
            .Include(booking => booking.StatusHistories)
            .SingleOrDefaultAsync(booking => booking.BookingId == bookingId && booking.Player!.UserId == userId, cancellationToken);
    }

    private async Task<Player?> GetOrCreatePlayerAsync(int userId, CancellationToken cancellationToken)
    {
        var player = await _dbContext.Players.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (player is not null) return player;
        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (user is null || !(user.UserType.Equals("Player", StringComparison.OrdinalIgnoreCase) || user.UserType.Equals("User", StringComparison.OrdinalIgnoreCase))) return null;
        player = new Player { UserId = userId, Prestige = 0, SkillLevel = 0 };
        _dbContext.Players.Add(player);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return player;
    }

    private Task ExpireHoldingAsync(Booking booking, string reason, CancellationToken cancellationToken)
    {
        var previous = booking.Status;
        booking.Status = "Expired";
        booking.HoldExpiresAt = null;
        foreach (var payment in booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation"))
        {
            var fromStatus = payment.Status;
            payment.Status = "Expired";
            payment.StatusHistories.Add(NewPaymentHistory(fromStatus, "Expired", "BookingExpired", reason, null));
        }
        booking.StatusHistories.Add(NewHistory(previous, "Expired", reason, null));
        return Task.CompletedTask;
    }

    private static BookingStatusHistory NewHistory(string? from, string to, string reason, int? actorUserId) => new()
    {
        FromStatus = from,
        ToStatus = to,
        Reason = reason,
        ActorUserId = actorUserId,
        ChangedAt = DateTime.UtcNow
    };

    private static BookingHoldingResponse MapBooking(Booking booking, Court court) => new()
    {
        BookingId = booking.BookingId,
        BookingCode = booking.BookingCode ?? $"PL-{booking.BookingId}",
        Status = booking.Status,
        CreatedAt = AsUtc(booking.CreatedAt),
        HoldExpiresAt = AsUtc(booking.HoldExpiresAt),
        VenueId = court.VenueId,
        VenueName = court.Venue.VenueName,
        Address = court.Venue.Address,
        CourtId = court.CourtId,
        CourtNumber = court.CourtNumber,
        StartTime = booking.StartTime,
        EndTime = booking.EndTime,
        DurationHours = (booking.EndTime - booking.StartTime).TotalHours,
        HourlyPrice = booking.HourlyPriceSnapshot,
        CourtAmount = booking.CourtAmount,
        TotalAmount = booking.TotalAmount,
        PaymentStatus = booking.Payments.OrderByDescending(item => item.PaymentId).Select(item => item.Status).FirstOrDefault() ?? "Pending",
        BankTransfer = booking.Payments.OrderByDescending(item => item.PaymentId).Select(MapTransfer).FirstOrDefault(),
        StatusHistory = booking.StatusHistories.OrderBy(item => item.ChangedAt).Select(item => new BookingStatusHistoryResponse { FromStatus = item.FromStatus, ToStatus = item.ToStatus, Reason = item.Reason, ChangedAt = AsUtc(item.ChangedAt) }).ToList()
    };

    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private void PublishBookingChanged(Booking booking, string status, string action) =>
        _scheduleRealtime.Publish(new ScheduleChangedEvent(booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, status, action));
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
    private static BankTransferResponse MapTransfer(Payment payment) => new()
    {
        PaymentId = payment.PaymentId,
        BookingId = payment.BookingId,
        PaymentStatus = payment.Status,
        Amount = payment.Amount,
        TransferCode = payment.TransferCode,
        TransferContent = payment.TransferContent,
        BankCode = payment.BankCode,
        BankName = payment.BankName,
        BankAccountNumber = payment.BankAccountNumber,
        BankAccountName = payment.BankAccountName,
        QrImageUrl = payment.QrImageUrl,
        ReceiptImageUrl = payment.ReceiptImageUrl,
        SubmittedAt = payment.SubmittedAt,
        VerifiedAt = payment.VerifiedAt,
        RejectionReason = payment.RejectionReason,
        History = payment.StatusHistories.OrderBy(item => item.CreatedAt).Select(item => new PaymentHistoryResponse
        {
            FromStatus = item.FromStatus,
            ToStatus = item.ToStatus,
            Action = item.Action,
            Reason = item.Reason,
            CreatedAt = item.CreatedAt
        }).ToList()
    };
    private static PaymentStatusHistory NewPaymentHistory(string? from, string to, string action, string? reason, int? actorUserId) => new()
    {
        FromStatus = from, ToStatus = to, Action = action, Reason = reason, ActorUserId = actorUserId, CreatedAt = DateTime.UtcNow
    };
    private static string BuildVietQrUrl(OwnerBankAccount account, double amount, string content)
    {
        var query = $"amount={Math.Round(amount):0}&addInfo={Uri.EscapeDataString(content)}&accountName={Uri.EscapeDataString(account.AccountHolderName)}";
        return $"https://img.vietqr.io/image/{Uri.EscapeDataString(account.BankCode)}-{Uri.EscapeDataString(account.AccountNumber)}-compact2.png?{query}";
    }
    private static double GetBasePrice(Venue venue) => double.TryParse(venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static double RoundMoney(double value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
