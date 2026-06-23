using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Staff")]
[Route("api/staff")]
public class StaffOperationsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly PaymentRealtimeNotifier _paymentRealtime;

    public StaffOperationsController(
        ApplicationDbContext dbContext,
        ScheduleRealtimeNotifier scheduleRealtime,
        PaymentRealtimeNotifier paymentRealtime)
    {
        _dbContext = dbContext;
        _scheduleRealtime = scheduleRealtime;
        _paymentRealtime = paymentRealtime;
    }

    [HttpGet("assignments")]
    public async Task<ActionResult<List<StaffAssignmentResponse>>> GetAssignments(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var assignments = await _dbContext.Staff.AsNoTracking()
            .Where(item => item.UserId == userId && item.IsActive)
            .Include(item => item.Venue)
            .OrderBy(item => item.Venue.VenueName)
            .ToListAsync(cancellationToken);
        return Ok(assignments.Select(item => new StaffAssignmentResponse
        {
            StaffId = item.StaffId, VenueId = item.VenueId, VenueName = item.Venue.VenueName,
            Role = item.Role, Permissions = SplitPermissions(item.Permissions)
        }).ToList());
    }

    [HttpGet("bookings/today")]
    public async Task<ActionResult<List<StaffBookingResponse>>> GetTodayBookings(
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var target = date ?? DateOnly.FromDateTime(DateTime.Now);
        var start = target.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        var bookings = await ScopedBookings(userId.Value, "ViewBookings")
            .Where(item => item.StartTime >= start && item.StartTime < end)
            .OrderBy(item => item.StartTime)
            .ToListAsync(cancellationToken);
        return Ok(bookings.Select(MapBooking).ToList());
    }

    [HttpGet("bookings/search")]
    public async Task<ActionResult<StaffBookingResponse>> SearchBooking(string code, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var normalized = code.Trim().ToUpper();
        if (normalized.Length < 3) return BadRequest(new { message = "Vui lòng nhập mã booking." });
        var booking = await ScopedBookings(userId.Value, "ViewBookings")
            .SingleOrDefaultAsync(item => item.BookingCode != null && item.BookingCode.ToUpper() == normalized, cancellationToken);
        return booking is null
            ? NotFound(new { message = "Không tìm thấy booking trong các cụm sân được phân công." })
            : Ok(MapBooking(booking));
    }

    [HttpGet("bookings/{bookingId:int}")]
    public async Task<ActionResult<StaffBookingResponse>> GetBooking(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var booking = await ScopedBookings(userId.Value, "ViewBookings")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        return booking is null
            ? NotFound(new { message = "Không tìm thấy booking trong các cụm sân được phân công." })
            : Ok(MapBooking(booking));
    }

    [HttpPost("bookings/{bookingId:int}/verify-code")]
    public async Task<ActionResult<StaffBookingResponse>> VerifyBookingCode(
        int bookingId,
        VerifyBookingCodeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var booking = await ScopedBookings(userId.Value, "VerifyBooking")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Booking không thuộc sân được phân công hoặc bạn chưa được cấp quyền xác minh mã." });
        if (booking.Status != "Confirmed") return Conflict(new { message = "Chỉ xác minh mã cho booking đã xác nhận." });
        if (!string.Equals(booking.BookingCode, request.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Mã booking không chính xác." });

        var operation = EnsureOperation(booking);
        if (operation.CheckInStatus is "CheckedIn" or "NoShow")
            return Conflict(new { message = "Booking đã hoàn tất xử lý check-in." });
        operation.CodeVerifiedAt = DateTime.UtcNow;
        operation.CodeVerifiedByUserId = userId;
        operation.UpdatedAt = DateTime.UtcNow;
        AddAudit(booking, userId.Value, $"BookingCodeVerified:{booking.BookingId}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        PublishBookingChanged(booking, "CodeVerified");
        return Ok(MapBooking(booking));
    }

    [HttpPost("bookings/{bookingId:int}/confirm-at-court-payment")]
    public async Task<ActionResult<StaffBookingResponse>> ConfirmAtCourtPayment(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"staff-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý." });
        var booking = await ScopedBookings(userId.Value, "ConfirmPayment")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Booking không thuộc sân được phân công hoặc bạn chưa được cấp quyền thu tiền." });
        if (booking.Status is "Cancelled" or "Expired") return Conflict(new { message = "Không thể thu tiền cho booking đã hủy hoặc hết hạn." });
        if (booking.Status != "Confirmed") return Conflict(new { message = "Booking phải được xác nhận trước khi thu tiền tại sân." });
        var payment = booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault();
        if (payment is null || payment.PaymentMethod != "AtCourt")
            return Conflict(new { message = "Booking không chọn phương thức thanh toán tại sân." });
        if (payment.Status == "Paid") return Ok(MapBooking(booking));
        if (payment.Status != "Pending") return Conflict(new { message = $"Không thể xác nhận thanh toán ở trạng thái {payment.Status}." });

        var now = DateTime.UtcNow;
        payment.Status = "Paid";
        payment.PaidAt = now;
        payment.VerifiedAt = now;
        payment.VerifiedByUserId = userId;
        payment.StatusHistories.Add(new PaymentStatusHistory
        {
            FromStatus = "Pending", ToStatus = "Paid", Action = "AtCourtPaymentConfirmed",
            Reason = "Staff xác nhận đã nhận tiền tại sân", ActorUserId = userId, CreatedAt = now
        });
        var operation = EnsureOperation(booking);
        operation.PaymentConfirmedAt = now;
        operation.PaymentConfirmedByUserId = userId;
        operation.UpdatedAt = now;
        AddAudit(booking, userId.Value, $"AtCourtPaymentConfirmed:{booking.BookingId}:{payment.PaymentId}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _paymentRealtime.Publish(new PaymentChangedEvent(
            payment.PaymentId, booking.BookingId, booking.Court.VenueId, payment.Status, "AtCourtConfirmed"));
        PublishBookingChanged(booking, "PaymentConfirmed");
        return Ok(MapBooking(booking));
    }

    [HttpPost("bookings/{bookingId:int}/check-in")]
    public async Task<ActionResult<StaffBookingResponse>> CheckIn(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"staff-checkin:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý." });
        var booking = await ScopedBookings(userId.Value, "CheckIn")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Booking không thuộc sân được phân công hoặc bạn chưa được cấp quyền check-in." });
        if (booking.MatchId.HasValue)
            return Conflict(new { message = "Đơn ghép trận phải xác nhận vào sân riêng cho từng người chơi." });
        if (booking.Status is "Cancelled" or "Expired") return Conflict(new { message = "Không thể check-in booking đã hủy hoặc hết hạn." });
        if (booking.Status != "Confirmed") return Conflict(new { message = "BookingStatus phải là Confirmed." });
        var operation = booking.Operation;
        if (operation?.CheckInStatus == "CheckedIn") return Conflict(new { message = "Booking đã được check-in trước đó." });
        if (operation?.CheckInStatus == "NoShow") return Conflict(new { message = "Booking đã được đánh dấu no-show." });
        if (operation is null || operation.CheckInStatus != "Ready") return Conflict(new { message = "CheckInStatus phải là Ready." });
        if (operation.CodeVerifiedAt is null) return Conflict(new { message = "Staff phải xác minh mã booking trước khi check-in." });
        if (!booking.Payments.Any(item => item.Status == "Paid")) return Conflict(new { message = "Booking chưa được xác nhận thanh toán." });
        var localNow = DateTime.Now;
        if (localNow < booking.StartTime.AddMinutes(-30) || localNow > booking.EndTime)
            return Conflict(new { message = "Ngoài thời gian check-in (từ 30 phút trước giờ chơi đến khi booking kết thúc)." });

        var now = DateTime.UtcNow;
        operation.CheckInStatus = "CheckedIn";
        operation.CheckedInAt = now;
        operation.CheckedInByUserId = userId;
        operation.UpdatedAt = now;

        AddAudit(booking, userId.Value, $"BookingCheckedIn:{booking.BookingId}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishBookingChanged(booking, "CheckedIn");
        return Ok(MapBooking(booking));
    }

    [HttpPost("bookings/{bookingId:int}/no-show")]
    public async Task<ActionResult<StaffBookingResponse>> MarkNoShow(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"staff-checkin:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý." });
        var booking = await ScopedBookings(userId.Value, "MarkNoShow")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Booking không thuộc sân được phân công hoặc bạn chưa được cấp quyền no-show." });
        if (booking.MatchId.HasValue)
            return Conflict(new { message = "Đơn ghép trận phải đánh dấu vắng mặt riêng cho từng người chơi." });
        if (booking.Status is "Cancelled" or "Expired") return Conflict(new { message = "Không thể đánh dấu no-show cho booking đã hủy hoặc hết hạn." });
        if (booking.Status != "Confirmed") return Conflict(new { message = "BookingStatus phải là Confirmed." });
        var operation = EnsureOperation(booking);
        if (operation.CheckInStatus == "CheckedIn") return Conflict(new { message = "Booking đã check-in, không thể đánh dấu no-show." });
        if (operation.CheckInStatus == "NoShow") return Conflict(new { message = "Booking đã được đánh dấu no-show trước đó." });
        if (DateTime.Now < booking.StartTime.AddMinutes(15))
            return Conflict(new { message = "Chỉ có thể đánh dấu no-show sau giờ bắt đầu 15 phút." });

        var now = DateTime.UtcNow;
        operation.CheckInStatus = "NoShow";
        operation.NoShowAt = now;
        operation.NoShowByUserId = userId;
        operation.UpdatedAt = now;
        AddAudit(booking, userId.Value, $"BookingMarkedNoShow:{booking.BookingId}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishBookingChanged(booking, "NoShow");
        return Ok(MapBooking(booking));
    }

    [HttpPost("bookings/{bookingId:int}/participants/{playerId:int}/check-in")]
    public async Task<ActionResult<StaffBookingResponse>> CheckInMatchParticipant(
        int bookingId,
        int playerId,
        CancellationToken cancellationToken)
    {
        return await UpdateMatchParticipantAttendance(
            bookingId, playerId, "Present", "CheckIn", cancellationToken);
    }

    [HttpPost("bookings/{bookingId:int}/participants/{playerId:int}/no-show")]
    public async Task<ActionResult<StaffBookingResponse>> MarkMatchParticipantNoShow(
        int bookingId,
        int playerId,
        CancellationToken cancellationToken)
    {
        return await UpdateMatchParticipantAttendance(
            bookingId, playerId, "Absent", "MarkNoShow", cancellationToken);
    }

    [HttpGet("notifications")]
    public async Task<ActionResult<List<StaffNotificationResponse>>> GetNotifications(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var start = today.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        var bookings = await ScopedBookings(userId.Value, "ViewBookings")
            .Where(item => item.StartTime >= start && item.StartTime < end && item.Status == "Confirmed")
            .OrderBy(item => item.StartTime)
            .ToListAsync(cancellationToken);
        var now = DateTime.Now;
        var notifications = new List<StaffNotificationResponse>();
        foreach (var booking in bookings)
        {
            var code = booking.BookingCode ?? $"PL-{booking.BookingId}";
            var payment = booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault();
            if (payment is { PaymentMethod: "AtCourt", Status: "Pending" })
                notifications.Add(new StaffNotificationResponse("Payment", code, $"{code} cần thu tiền tại sân.", booking.BookingId, booking.StartTime));
            if (booking.Operation?.CheckInStatus is not ("CheckedIn" or "NoShow") && booking.StartTime >= now && booking.StartTime <= now.AddMinutes(30))
                notifications.Add(new StaffNotificationResponse("Upcoming", code, $"{code} sắp đến giờ, chuẩn bị check-in.", booking.BookingId, booking.StartTime));
            if (booking.Operation?.CheckInStatus is not ("CheckedIn" or "NoShow") && now >= booking.StartTime.AddMinutes(15))
                notifications.Add(new StaffNotificationResponse("Overdue", code, $"{code} chưa check-in, hãy kiểm tra no-show.", booking.BookingId, booking.StartTime));
        }
        return Ok(notifications.OrderBy(item => item.StartTime).ToList());
    }

    private IQueryable<Booking> ScopedBookings(int userId, string permission) => _dbContext.Bookings
        .Include(item => item.Operation)
        .Include(item => item.Payments).ThenInclude(item => item.StatusHistories)
        .Include(item => item.Player).ThenInclude(item => item!.User)
        .Include(item => item.Match).ThenInclude(item => item!.MatchParticipants).ThenInclude(item => item.Player).ThenInclude(item => item.User)
        .Include(item => item.Match).ThenInclude(item => item!.MatchCheckIns)
        .Include(item => item.Court).ThenInclude(item => item.Venue)
        .Where(item => item.PlayerId != null && item.Court.Venue.Staff.Any(staff =>
            staff.UserId == userId && staff.IsActive && staff.Permissions.Contains(permission)));

    private BookingOperation EnsureOperation(Booking booking)
    {
        if (booking.Operation is not null) return booking.Operation;
        var operation = new BookingOperation { BookingId = booking.BookingId, CheckInStatus = "Ready", UpdatedAt = DateTime.UtcNow };
        booking.Operation = operation;
        _dbContext.BookingOperations.Add(operation);
        return operation;
    }

    private async Task<ActionResult<StaffBookingResponse>> UpdateMatchParticipantAttendance(
        int bookingId,
        int playerId,
        string attendanceStatus,
        string permission,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                _dbContext, transaction, $"staff-checkin:{bookingId}", cancellationToken))
            return Conflict(new { message = "Đơn đang được xử lý." });

        var booking = await ScopedBookings(userId.Value, permission)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking?.MatchId is null || booking.Match is null)
            return NotFound(new { message = "Không tìm thấy đơn ghép trận thuộc sân được phân công." });
        if (booking.Status != "Confirmed")
            return Conflict(new { message = "Đơn ghép trận phải ở trạng thái đã xác nhận." });

        var operation = EnsureOperation(booking);
        if (operation.CodeVerifiedAt is null)
            return Conflict(new { message = "Nhân viên phải xác minh mã đơn trước khi điểm danh." });

        var participant = booking.Match.MatchParticipants
            .SingleOrDefault(item => item.PlayerId == playerId && item.Status == "Accepted");
        if (participant is null)
            return NotFound(new { message = "Người chơi không thuộc nhóm đã được chấp nhận." });

        var latestPayment = booking.Payments
            .Where(item => item.PayerId == playerId)
            .OrderByDescending(item => item.PaymentId)
            .FirstOrDefault();
        if (latestPayment?.Status != "Paid")
            return Conflict(new { message = "Người chơi chưa được xác nhận thanh toán." });

        var localNow = DateTime.Now;
        if (attendanceStatus == "Present"
            && (localNow < booking.StartTime.AddMinutes(-30) || localNow > booking.EndTime))
            return Conflict(new { message = "Chỉ được xác nhận vào sân từ 30 phút trước giờ chơi đến khi trận kết thúc." });
        if (attendanceStatus == "Absent" && localNow < booking.StartTime.AddMinutes(15))
            return Conflict(new { message = "Chỉ được đánh dấu vắng mặt sau giờ bắt đầu 15 phút." });

        var attendance = booking.Match.MatchCheckIns
            .SingleOrDefault(item => item.PlayerId == playerId);
        if (attendance is not null)
            return Conflict(new
            {
                message = attendance.Status == "Present"
                    ? "Người chơi đã được xác nhận vào sân."
                    : "Người chơi đã được đánh dấu vắng mặt."
            });

        var staffId = await _dbContext.Staff
            .Where(item => item.UserId == userId.Value
                && item.VenueId == booking.Court.VenueId
                && item.IsActive)
            .Select(item => (int?)item.StaffId)
            .FirstOrDefaultAsync(cancellationToken);
        var now = DateTime.UtcNow;
        booking.Match.MatchCheckIns.Add(new MatchCheckIn
        {
            MatchId = booking.MatchId.Value,
            PlayerId = playerId,
            StaffId = staffId,
            Status = attendanceStatus,
            CheckedInAt = now
        });

        var acceptedPlayerIds = booking.Match.MatchParticipants
            .Where(item => item.Status == "Accepted")
            .Select(item => item.PlayerId)
            .ToHashSet();
        var processedAttendances = booking.Match.MatchCheckIns
            .Where(item => acceptedPlayerIds.Contains(item.PlayerId))
            .ToList();
        if (processedAttendances.Count == acceptedPlayerIds.Count)
        {
            if (processedAttendances.Any(item => item.Status == "Present"))
            {
                operation.CheckInStatus = "CheckedIn";
                operation.CheckedInAt = now;
                operation.CheckedInByUserId = userId;
            }
            else
            {
                operation.CheckInStatus = "NoShow";
                operation.NoShowAt = now;
                operation.NoShowByUserId = userId;
            }
        }
        else
        {
            operation.CheckInStatus = "Ready";
        }
        operation.UpdatedAt = now;

        AddAudit(
            booking,
            userId.Value,
            attendanceStatus == "Present"
                ? $"MatchPlayerCheckedIn:{booking.BookingId}:{playerId}"
                : $"MatchPlayerMarkedAbsent:{booking.BookingId}:{playerId}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishBookingChanged(
            booking,
            attendanceStatus == "Present" ? "MatchPlayerCheckedIn" : "MatchPlayerAbsent");
        return Ok(MapBooking(booking));
    }

    private void AddAudit(Booking booking, int userId, string action) => _dbContext.VenueAuditLogs.Add(new VenueAuditLog
    {
        VenueId = booking.Court.VenueId, ActorId = userId, Action = action, Timestamp = DateTime.UtcNow
    });

    private void PublishBookingChanged(Booking booking, string action) =>
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            booking.Court.VenueId,
            booking.CourtId,
            booking.StartTime,
            booking.EndTime,
            booking.Status,
            action));

    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private static string[] SplitPermissions(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static StaffBookingResponse MapBooking(Booking booking)
    {
        var operation = booking.Operation;
        var payment = booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault();
        var isMatchBooking = booking.MatchId.HasValue;
        var acceptedParticipants = booking.Match?.MatchParticipants
            .Where(item => item.Status == "Accepted")
            .ToList() ?? [];
        var paymentStatus = isMatchBooking
            ? GetMatchPaymentStatus(booking)
            : payment?.Status ?? "Pending";
        var matchAttendances = booking.Match?.MatchCheckIns
            .ToDictionary(item => item.PlayerId) ?? [];
        var localNow = DateTime.Now;
        var checkInStatus = booking.Status is "Cancelled" or "Expired"
            ? "Cancelled"
            : operation?.CheckInStatus ?? (booking.Status == "Confirmed" && localNow >= booking.StartTime.AddMinutes(-30) ? "Ready" : "NotOpen");
        return new StaffBookingResponse
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode ?? $"PL-{booking.BookingId}",
            BookingType = isMatchBooking ? "Match" : "Court",
            MatchId = booking.MatchId,
            BookingStatus = booking.Status,
            CheckInStatus = checkInStatus,
            PaymentStatus = paymentStatus,
            PaymentMethod = isMatchBooking ? "GroupOnline" : payment?.PaymentMethod,
            Amount = booking.TotalAmount,
            VenueId = booking.Court.VenueId,
            VenueName = booking.Court.Venue.VenueName,
            Address = booking.Court.Venue.Address,
            CourtId = booking.CourtId,
            CourtNumber = booking.Court.CourtNumber,
            PlayerName = booking.Player?.User.Username ?? "Khách",
            ParticipantCount = isMatchBooking ? acceptedParticipants.Count : 1,
            CheckedInParticipantCount = isMatchBooking
                ? booking.Match?.MatchCheckIns.Count(item => item.Status == "Present") ?? 0
                : checkInStatus == "CheckedIn" ? 1 : 0,
            Participants = acceptedParticipants
                .OrderByDescending(item => item.IsHost)
                .ThenBy(item => item.RequestedAt)
                .Select(item =>
                {
                    var latestPlayerPayment = booking.Payments
                        .Where(paymentItem => paymentItem.PayerId == item.PlayerId)
                        .OrderByDescending(paymentItem => paymentItem.PaymentId)
                        .FirstOrDefault();
                    matchAttendances.TryGetValue(item.PlayerId, out var attendance);
                    return new StaffMatchParticipantResponse
                    {
                        PlayerId = item.PlayerId,
                        PlayerName = item.Player.User.Username,
                        IsHost = item.IsHost,
                        PaymentStatus = latestPlayerPayment?.Status ?? "Pending",
                        AttendanceStatus = attendance?.Status ?? "Pending",
                        AttendanceAt = AsUtc(attendance?.CheckedInAt)
                    };
                })
                .ToList(),
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            IsCheckInWindowOpen = localNow >= booking.StartTime.AddMinutes(-30) && localNow <= booking.EndTime,
            CanMarkNoShow = localNow >= booking.StartTime.AddMinutes(15) && checkInStatus is not ("CheckedIn" or "NoShow"),
            CodeVerifiedAt = AsUtc(operation?.CodeVerifiedAt),
            PaymentConfirmedAt = AsUtc(operation?.PaymentConfirmedAt),
            CheckedInAt = AsUtc(operation?.CheckedInAt),
            NoShowAt = AsUtc(operation?.NoShowAt)
        };
    }

    private static bool AreAllMatchPlayersPaid(Booking booking)
    {
        if (booking.Match is null) return false;
        var playerIds = booking.Match.MatchParticipants
            .Where(item => item.Status == "Accepted")
            .Select(item => item.PlayerId)
            .Distinct()
            .ToList();
        return playerIds.Count > 0 && playerIds.All(playerId =>
            booking.Payments
                .Where(item => item.PayerId == playerId)
                .OrderByDescending(item => item.PaymentId)
                .FirstOrDefault()?.Status == "Paid");
    }

    private static string GetMatchPaymentStatus(Booking booking)
    {
        if (AreAllMatchPlayersPaid(booking)) return "Paid";
        if (booking.Payments.Any(item => item.Status == "WaitingForConfirmation")) return "WaitingForConfirmation";
        if (booking.Payments.Any(item => item.Status == "Failed")) return "Failed";
        return "Pending";
    }

    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}

public record VerifyBookingCodeRequest(string Code);
public record StaffNotificationResponse(string Type, string Title, string Message, int BookingId, DateTime StartTime);
public class StaffAssignmentResponse
{
    public int StaffId { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string[] Permissions { get; set; } = Array.Empty<string>();
}
public class StaffBookingResponse
{
    public int BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public string BookingType { get; set; } = "Court";
    public int? MatchId { get; set; }
    public string BookingStatus { get; set; } = string.Empty;
    public string CheckInStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public double Amount { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int ParticipantCount { get; set; } = 1;
    public int CheckedInParticipantCount { get; set; }
    public List<StaffMatchParticipantResponse> Participants { get; set; } = [];
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsCheckInWindowOpen { get; set; }
    public bool CanMarkNoShow { get; set; }
    public DateTime? CodeVerifiedAt { get; set; }
    public DateTime? PaymentConfirmedAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? NoShowAt { get; set; }
}

public class StaffMatchParticipantResponse
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string AttendanceStatus { get; set; } = "Pending";
    public DateTime? AttendanceAt { get; set; }
}
