using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Matches;
using PicklinkBackend.Services.Payments;
using PicklinkBackend.Services.Schedules;

namespace PicklinkBackend.Services.Staff;

public sealed class StaffOperationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly PaymentRealtimeNotifier _paymentRealtime;
    private readonly MatchRealtimeNotifier _matchRealtime;

    public StaffOperationService(
        ApplicationDbContext dbContext,
        ScheduleRealtimeNotifier scheduleRealtime,
        PaymentRealtimeNotifier paymentRealtime,
        MatchRealtimeNotifier matchRealtime)
    {
        _dbContext = dbContext;
        _scheduleRealtime = scheduleRealtime;
        _paymentRealtime = paymentRealtime;
        _matchRealtime = matchRealtime;
    }

    public async Task<StaffOperationResult<List<StaffAssignmentResponse>>> ListAssignmentsAsync(
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<List<StaffAssignmentResponse>>.Unauthorized();

        var assignments = await _dbContext.Staff.AsNoTracking()
            .Where(item => item.UserId == userId && item.IsActive)
            .Include(item => item.Venue)
            .OrderBy(item => item.Venue.VenueName)
            .ToListAsync(cancellationToken);

        return StaffOperationResult<List<StaffAssignmentResponse>>.Success(assignments.Select(item => new StaffAssignmentResponse
        {
            StaffId = item.StaffId,
            VenueId = item.VenueId,
            VenueName = item.Venue.VenueName,
            Role = item.Role,
            Permissions = SplitPermissions(item.Permissions)
        }).ToList());
    }

    public async Task<StaffOperationResult<PaginatedResponse<StaffBookingResponse>>> ListTodayBookingsAsync(
        int? userId,
        DateOnly? date,
        string? bookingType,
        int? venueId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<PaginatedResponse<StaffBookingResponse>>.Unauthorized();

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var target = date ?? DateOnly.FromDateTime(DateTime.Now);
        var start = target.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        var query = ScopedBookings(userId.Value, "ViewBookings", includePayments: false)
            .AsNoTracking()
            .Where(item => item.StartTime >= start && item.StartTime < end);

        if (bookingType?.Equals("Court", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(item => item.MatchId == null);
        else if (bookingType?.Equals("Match", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(item => item.MatchId != null);

        if (venueId.HasValue)
            query = query.Where(item => item.Court.VenueId == venueId.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var bookings = await query
            .OrderBy(item => item.StartTime)
            .ThenBy(item => item.BookingId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var bookingIds = bookings.Select(item => item.BookingId).ToList();
        var paymentRows = await _dbContext.Payments.AsNoTracking()
            .Where(item => bookingIds.Contains(item.BookingId))
            .Select(item => new
            {
                item.PaymentId,
                item.BookingId,
                item.PayerId,
                item.PaymentMethod,
                item.Status
            })
            .ToListAsync(cancellationToken);
        var paymentsByBooking = paymentRows.ToLookup(item => item.BookingId);
        foreach (var booking in bookings)
            booking.Payments = paymentsByBooking[booking.BookingId]
                .Select(item => new Payment
                {
                    PaymentId = item.PaymentId,
                    BookingId = item.BookingId,
                    PayerId = item.PayerId,
                    PaymentMethod = item.PaymentMethod,
                    Status = item.Status
                })
                .ToList();

        return StaffOperationResult<PaginatedResponse<StaffBookingResponse>>.Success(
            Pagination.Create(bookings.Select(MapBooking), totalCount, page, pageSize));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> SearchBookingAsync(
        int? userId,
        string code,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        var normalized = code.Trim().ToUpper();
        if (normalized.Length < 3)
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Vui long nhap ma booking.");

        var booking = await ScopedBookings(userId.Value, "VerifyBooking", "CheckIn")
            .SingleOrDefaultAsync(item =>
                (item.BookingCode != null && item.BookingCode == normalized)
                || item.CheckInGroups.Any(group => group.CheckInCode == normalized), cancellationToken);

        return booking is null
            ? StaffOperationResult<StaffBookingResponse>.NotFound("Khong tim thay booking trong cac cum san duoc phan cong.")
            : StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> GetBookingAsync(
        int? userId,
        int bookingId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        var booking = await ScopedBookings(userId.Value, "ViewBookings")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);

        return booking is null
            ? StaffOperationResult<StaffBookingResponse>.NotFound("Khong tim thay booking trong cac cum san duoc phan cong.")
            : StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> VerifyBookingCodeByCodeAsync(
        int? userId,
        VerifyBookingCodeRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        var normalized = request.Code.Trim().ToUpper();
        if (normalized.Length < 3)
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Vui long nhap ma booking.");

        var booking = await ScopedBookings(userId.Value, "VerifyBooking", "CheckIn")
            .SingleOrDefaultAsync(item =>
                (item.BookingCode != null && item.BookingCode == normalized)
                || item.CheckInGroups.Any(group => group.CheckInCode == normalized), cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Khong tim thay booking trong cac cum san duoc phan cong.");
        if (booking.Status != "Confirmed")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Chi xac minh ma cho booking da xac nhan.");

        var group = booking.CheckInGroups.SingleOrDefault(item => item.CheckInCode == normalized);
        if (group is not null)
        {
            if (group.CheckInStatus is "CheckedIn" or "NoShow")
                return StaffOperationResult<StaffBookingResponse>.Conflict("Nhom slot da hoan tat check-in.");
            group.CodeVerifiedAt = DateTime.UtcNow;
            group.CodeVerifiedByUserId = userId;
            group.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var operation = EnsureOperation(booking);
            if (operation.CheckInStatus is "CheckedIn" or "NoShow")
                return StaffOperationResult<StaffBookingResponse>.Conflict("Booking da hoan tat xu ly check-in.");
            operation.CodeVerifiedAt = DateTime.UtcNow;
            operation.CodeVerifiedByUserId = userId;
            operation.UpdatedAt = DateTime.UtcNow;
            AddAudit(booking, userId.Value, $"BookingCodeVerified:{booking.BookingId}");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        PublishBookingChanged(booking, "CodeVerified");
        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> VerifyBookingCodeAsync(
        int? userId,
        int bookingId,
        VerifyBookingCodeRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        var booking = await ScopedBookings(userId.Value, "VerifyBooking", "CheckIn")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Booking khong thuoc san duoc phan cong hoac ban chua duoc cap quyen xac minh ma.");
        if (booking.Status != "Confirmed")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Chi xac minh ma cho booking da xac nhan.");
        if (!string.Equals(booking.BookingCode, request.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Ma booking khong chinh xac.");

        var operation = EnsureOperation(booking);
        if (operation.CheckInStatus is "CheckedIn" or "NoShow")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Booking da hoan tat xu ly check-in.");

        operation.CodeVerifiedAt = DateTime.UtcNow;
        operation.CodeVerifiedByUserId = userId;
        operation.UpdatedAt = DateTime.UtcNow;
        AddAudit(booking, userId.Value, $"BookingCodeVerified:{booking.BookingId}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        PublishBookingChanged(booking, "CodeVerified");

        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> VerifyCheckInGroupCodeAsync(int? userId, int bookingId, int checkInGroupId, VerifyBookingCodeRequest request, CancellationToken cancellationToken)
    {
        var result = await LoadCheckInGroupAsync(userId, bookingId, checkInGroupId, "VerifyBooking", cancellationToken, "CheckIn");
        if (result.Error is not null) return result.Error;
        var booking = result.Booking!;
        var group = result.Group!;
        if (!string.Equals(group.CheckInCode, request.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Ma check-in khong chinh xac.");
        if (group.CheckInStatus is "CheckedIn" or "NoShow") return StaffOperationResult<StaffBookingResponse>.Conflict("Nhom slot da hoan tat check-in.");
        group.CodeVerifiedAt = DateTime.UtcNow;
        group.CodeVerifiedByUserId = userId;
        group.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> CheckInGroupAsync(int? userId, int bookingId, int checkInGroupId, CancellationToken cancellationToken)
    {
        var result = await LoadCheckInGroupAsync(userId, bookingId, checkInGroupId, "CheckIn", cancellationToken);
        if (result.Error is not null) return result.Error;
        var booking = result.Booking!;
        var group = result.Group!;
        if (group.CodeVerifiedAt is null) return StaffOperationResult<StaffBookingResponse>.Conflict("Staff phai xac minh ma check-in truoc.");
        if (group.CheckInStatus != "Ready") return StaffOperationResult<StaffBookingResponse>.Conflict("Nhom slot khong san sang check-in.");
        if (DateTime.Now < group.StartTime.AddMinutes(-30) || DateTime.Now > group.EndTime) return StaffOperationResult<StaffBookingResponse>.Conflict("Ngoai thoi gian check-in.");
        var now = DateTime.UtcNow;
        group.CheckInStatus = "CheckedIn";
        group.CheckedInAt = now;
        group.CheckedInByUserId = userId;
        group.UpdatedAt = now;
        SyncBookingCheckInStatusFromGroups(booking, userId!.Value, now);
        AddAudit(booking, userId.Value, $"CheckInGroupCheckedIn:{booking.BookingId}:{group.BookingCheckInGroupId}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        PublishBookingChanged(booking, "CheckInGroupCheckedIn");
        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> MarkGroupNoShowAsync(int? userId, int bookingId, int checkInGroupId, CancellationToken cancellationToken)
    {
        var result = await LoadCheckInGroupAsync(userId, bookingId, checkInGroupId, "MarkNoShow", cancellationToken);
        if (result.Error is not null) return result.Error;
        var booking = result.Booking!;
        var group = result.Group!;
        if (group.CheckInStatus is "CheckedIn" or "NoShow") return StaffOperationResult<StaffBookingResponse>.Conflict("Nhom slot da hoan tat xu ly.");
        if (DateTime.Now < group.StartTime.AddMinutes(15)) return StaffOperationResult<StaffBookingResponse>.Conflict("Chi co the danh dau no-show sau gio bat dau 15 phut.");
        var now = DateTime.UtcNow;
        group.CheckInStatus = "NoShow";
        group.NoShowAt = now;
        group.NoShowByUserId = userId;
        group.UpdatedAt = now;
        SyncBookingCheckInStatusFromGroups(booking, userId!.Value, now);
        AddAudit(booking, userId.Value, $"CheckInGroupMarkedNoShow:{booking.BookingId}:{group.BookingCheckInGroupId}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        PublishBookingChanged(booking, "CheckInGroupNoShow");
        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> ConfirmAtCourtPaymentAsync(
        int? userId,
        int bookingId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"staff-payment:{bookingId}", cancellationToken))
            return StaffOperationResult<StaffBookingResponse>.Conflict("Booking dang duoc xu ly.");

        var booking = await ScopedBookings(userId.Value, "ConfirmPayment")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Booking khong thuoc san duoc phan cong hoac ban chua duoc cap quyen thu tien.");
        if (booking.Status is "Cancelled" or "Expired")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Khong the thu tien cho booking da huy hoac het han.");
        if (booking.Status != "Confirmed")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Booking phai duoc xac nhan truoc khi thu tien tai san.");

        var payment = booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault();
        if (payment is null || payment.PaymentMethod != "AtCourt")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Booking khong chon phuong thuc thanh toan tai san.");
        if (payment.Status == "Paid")
            return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
        if (payment.Status != "Pending")
            return StaffOperationResult<StaffBookingResponse>.Conflict($"Khong the xac nhan thanh toan o trang thai {payment.Status}.");

        var now = DateTime.UtcNow;
        payment.Status = "Paid";
        payment.PaidAt = now;
        payment.VerifiedAt = now;
        payment.VerifiedByUserId = userId;
        payment.StatusHistories.Add(new PaymentStatusHistory
        {
            FromStatus = "Pending",
            ToStatus = "Paid",
            Action = "AtCourtPaymentConfirmed",
            Reason = "Staff xac nhan da nhan tien tai san",
            ActorUserId = userId,
            CreatedAt = now
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

        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> CheckInAsync(
        int? userId,
        int bookingId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"staff-checkin:{bookingId}", cancellationToken))
            return StaffOperationResult<StaffBookingResponse>.Conflict("Booking dang duoc xu ly.");

        var booking = await ScopedBookings(userId.Value, "CheckIn")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Booking khong thuoc san duoc phan cong hoac ban chua duoc cap quyen check-in.");
        if (booking.MatchId.HasValue)
            return StaffOperationResult<StaffBookingResponse>.Conflict("Don ghep tran phai xac nhan vao san rieng cho tung nguoi choi.");
        if (booking.Status is "Cancelled" or "Expired")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Khong the check-in booking da huy hoac het han.");
        if (booking.Status != "Confirmed")
            return StaffOperationResult<StaffBookingResponse>.Conflict("BookingStatus phai la Confirmed.");

        var operation = booking.Operation;
        if (operation?.CheckInStatus == "CheckedIn")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Booking da duoc check-in truoc do.");
        if (operation?.CheckInStatus == "NoShow")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Booking da duoc danh dau no-show.");
        if (operation is null || operation.CheckInStatus != "Ready")
            return StaffOperationResult<StaffBookingResponse>.Conflict("CheckInStatus phai la Ready.");
        if (operation.CodeVerifiedAt is null)
            return StaffOperationResult<StaffBookingResponse>.Conflict("Staff phai xac minh ma booking truoc khi check-in.");
        if (!booking.Payments.Any(item => item.Status == "Paid"))
            return StaffOperationResult<StaffBookingResponse>.Conflict("Booking chua duoc xac nhan thanh toan.");

        var localNow = DateTime.Now;
        if (localNow < booking.StartTime.AddMinutes(-30) || localNow > booking.EndTime)
            return StaffOperationResult<StaffBookingResponse>.Conflict("Ngoai thoi gian check-in.");

        var now = DateTime.UtcNow;
        operation.CheckInStatus = "CheckedIn";
        operation.CheckedInAt = now;
        operation.CheckedInByUserId = userId;
        operation.UpdatedAt = now;

        AddAudit(booking, userId.Value, $"BookingCheckedIn:{booking.BookingId}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishBookingChanged(booking, "CheckedIn");

        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> MarkNoShowAsync(
        int? userId,
        int bookingId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"staff-checkin:{bookingId}", cancellationToken))
            return StaffOperationResult<StaffBookingResponse>.Conflict("Booking dang duoc xu ly.");

        var booking = await ScopedBookings(userId.Value, "MarkNoShow")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Booking khong thuoc san duoc phan cong hoac ban chua duoc cap quyen no-show.");
        if (booking.MatchId.HasValue)
            return StaffOperationResult<StaffBookingResponse>.Conflict("Don ghep tran phai danh dau vang mat rieng cho tung nguoi choi.");
        if (booking.Status is "Cancelled" or "Expired")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Khong the danh dau no-show cho booking da huy hoac het han.");
        if (booking.Status != "Confirmed")
            return StaffOperationResult<StaffBookingResponse>.Conflict("BookingStatus phai la Confirmed.");

        var operation = EnsureOperation(booking);
        if (operation.CheckInStatus == "CheckedIn")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Booking da check-in, khong the danh dau no-show.");
        if (operation.CheckInStatus == "NoShow")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Booking da duoc danh dau no-show truoc do.");
        if (DateTime.Now < booking.StartTime.AddMinutes(15))
            return StaffOperationResult<StaffBookingResponse>.Conflict("Chi co the danh dau no-show sau gio bat dau 15 phut.");

        var now = DateTime.UtcNow;
        operation.CheckInStatus = "NoShow";
        operation.NoShowAt = now;
        operation.NoShowByUserId = userId;
        operation.UpdatedAt = now;
        AddAudit(booking, userId.Value, $"BookingMarkedNoShow:{booking.BookingId}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishBookingChanged(booking, "NoShow");

        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
    }

    public Task<StaffOperationResult<StaffBookingResponse>> CheckInMatchParticipantAsync(
        int? userId,
        int bookingId,
        int playerId,
        CancellationToken cancellationToken) =>
        UpdateMatchParticipantAttendanceAsync(userId, bookingId, playerId, "Present", "CheckIn", cancellationToken);

    public Task<StaffOperationResult<StaffBookingResponse>> MarkMatchParticipantNoShowAsync(
        int? userId,
        int bookingId,
        int playerId,
        CancellationToken cancellationToken) =>
        UpdateMatchParticipantAttendanceAsync(userId, bookingId, playerId, "Absent", "MarkNoShow", cancellationToken);

    public async Task<StaffOperationResult<List<StaffNotificationResponse>>> ListNotificationsAsync(
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<List<StaffNotificationResponse>>.Unauthorized();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var start = today.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        var bookings = await ScopedBookings(userId.Value, "ViewBookings", includePayments: false)
            .Where(item => item.StartTime >= start && item.StartTime < end && item.Status == "Confirmed")
            .OrderBy(item => item.StartTime)
            .Select(item => new
            {
                item.BookingId,
                item.BookingCode,
                item.StartTime,
                CheckInStatus = item.Operation == null ? null : item.Operation.CheckInStatus,
                PaymentMethod = item.Payments
                    .OrderByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.PaymentMethod)
                    .FirstOrDefault(),
                PaymentStatus = item.Payments
                    .OrderByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.Status)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.Now;
        var notifications = new List<StaffNotificationResponse>();
        foreach (var booking in bookings)
        {
            var code = booking.BookingCode ?? $"PL-{booking.BookingId}";
            if (booking.PaymentMethod == "AtCourt" && booking.PaymentStatus == "Pending")
                notifications.Add(new StaffNotificationResponse("Payment", code, $"{code} can thu tien tai san.", booking.BookingId, booking.StartTime));
            if (booking.CheckInStatus is not ("CheckedIn" or "NoShow") && booking.StartTime >= now && booking.StartTime <= now.AddMinutes(30))
                notifications.Add(new StaffNotificationResponse("Upcoming", code, $"{code} sap den gio, chuan bi check-in.", booking.BookingId, booking.StartTime));
            if (booking.CheckInStatus is not ("CheckedIn" or "NoShow") && now >= booking.StartTime.AddMinutes(15))
                notifications.Add(new StaffNotificationResponse("Overdue", code, $"{code} chua check-in, hay kiem tra no-show.", booking.BookingId, booking.StartTime));
        }

        return StaffOperationResult<List<StaffNotificationResponse>>.Success(
            notifications.OrderBy(item => item.StartTime).ToList());
    }

    private IQueryable<Booking> ScopedBookings(
        int userId,
        string permission,
        string? alternatePermission = null,
        bool includePayments = true)
    {
        var permissionToken = $",{permission},";
        var alternatePermissionToken = alternatePermission is null ? null : $",{alternatePermission},";

        IQueryable<Booking> query = _dbContext.Bookings
        .AsSplitQuery()
        .Include(item => item.Operation)
        .Include(item => item.CheckInGroups).ThenInclude(group => group.Court)
        .Include(item => item.Player).ThenInclude(item => item!.User)
        .Include(item => item.Match).ThenInclude(item => item!.MatchParticipants).ThenInclude(item => item.Player).ThenInclude(item => item.User)
        .Include(item => item.Match).ThenInclude(item => item!.MatchCheckIns)
        .Include(item => item.Court).ThenInclude(item => item.Venue);
        if (includePayments) query = query.Include(item => item.Payments);

        return query.Where(item =>
            item.TicketSession == null
            && (item.Court.Venue.Owner.UserId == userId
                || item.Court.Venue.Staff.Any(staff =>
                    staff.UserId == userId && staff.IsActive &&
                    (("," + staff.Permissions + ",").Contains(permissionToken)
                        || (alternatePermissionToken != null && ("," + staff.Permissions + ",").Contains(alternatePermissionToken))))));
    }

    private async Task<(Booking? Booking, BookingCheckInGroup? Group, StaffOperationResult<StaffBookingResponse>? Error)> LoadCheckInGroupAsync(int? userId, int bookingId, int checkInGroupId, string permission, CancellationToken cancellationToken, string? alternatePermission = null)
    {
        if (userId is null) return (null, null, StaffOperationResult<StaffBookingResponse>.Unauthorized());
        var booking = await ScopedBookings(userId.Value, permission, alternatePermission)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null) return (null, null, StaffOperationResult<StaffBookingResponse>.NotFound("Khong tim thay booking thuoc san duoc phan cong."));
        if (booking.Status != "Confirmed" || !booking.Payments.Any(item => item.Status == "Paid"))
            return (null, null, StaffOperationResult<StaffBookingResponse>.Conflict("Booking phai da thanh toan va xac nhan."));
        var group = booking.CheckInGroups.SingleOrDefault(item => item.BookingCheckInGroupId == checkInGroupId);
        return group is null
            ? (null, null, StaffOperationResult<StaffBookingResponse>.NotFound("Khong tim thay nhom check-in."))
            : (booking, group, null);
    }

    private BookingOperation EnsureOperation(Booking booking)
    {
        if (booking.Operation is not null) return booking.Operation;

        var operation = new BookingOperation
        {
            BookingId = booking.BookingId,
            CheckInStatus = "Ready",
            UpdatedAt = DateTime.UtcNow
        };
        booking.Operation = operation;
        _dbContext.BookingOperations.Add(operation);
        return operation;
    }

    private void SyncBookingCheckInStatusFromGroups(Booking booking, int userId, DateTime now)
    {
        var operation = EnsureOperation(booking);
        var allProcessed = booking.CheckInGroups.Count > 0
            && booking.CheckInGroups.All(group => group.CheckInStatus is "CheckedIn" or "NoShow");

        if (!allProcessed)
        {
            operation.CheckInStatus = "Ready";
        }
        else if (booking.CheckInGroups.Any(group => group.CheckInStatus == "CheckedIn"))
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

        operation.UpdatedAt = now;
    }

    private async Task<StaffOperationResult<StaffBookingResponse>> UpdateMatchParticipantAttendanceAsync(
        int? userId,
        int bookingId,
        int playerId,
        string attendanceStatus,
        string permission,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                _dbContext, transaction, $"staff-checkin:{bookingId}", cancellationToken))
            return StaffOperationResult<StaffBookingResponse>.Conflict("Don dang duoc xu ly.");

        var booking = await ScopedBookings(userId.Value, permission)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking?.MatchId is null || booking.Match is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Khong tim thay don ghep tran thuoc san duoc phan cong.");
        if (booking.Status != "Confirmed")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Don ghep tran phai o trang thai da xac nhan.");

        var operation = EnsureOperation(booking);
        if (attendanceStatus == "Present" && operation.CodeVerifiedAt is null)
            return StaffOperationResult<StaffBookingResponse>.Conflict("Nhan vien phai xac minh ma don truoc khi diem danh.");

        var participant = booking.Match.MatchParticipants
            .SingleOrDefault(item => item.PlayerId == playerId && (item.Status == "Approved" || item.Status == "Accepted"));
        if (participant is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Nguoi choi khong thuoc nhom da duoc chap nhan.");

        var latestPayment = booking.Payments
            .Where(item => item.PayerId == playerId)
            .OrderByDescending(item => item.PaymentId)
            .FirstOrDefault();
        if (latestPayment?.Status != "Paid")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Nguoi choi chua duoc xac nhan thanh toan.");

        var localNow = DateTime.Now;
        if (attendanceStatus == "Present"
            && (localNow < booking.StartTime.AddMinutes(-30) || localNow > booking.EndTime))
            return StaffOperationResult<StaffBookingResponse>.Conflict("Chi duoc xac nhan vao san tu 30 phut truoc gio choi den khi tran ket thuc.");
        if (attendanceStatus == "Absent" && localNow < booking.StartTime.AddMinutes(15))
            return StaffOperationResult<StaffBookingResponse>.Conflict("Chi duoc danh dau vang mat sau gio bat dau 15 phut.");

        var attendance = booking.Match.MatchCheckIns
            .SingleOrDefault(item => item.PlayerId == playerId);
        if (attendance is not null)
            return StaffOperationResult<StaffBookingResponse>.Conflict(
                attendance.Status == "Present"
                    ? "Nguoi choi da duoc xac nhan vao san."
                    : "Nguoi choi da duoc danh dau vang mat.");

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
            .Where(item => item.Status == "Approved" || item.Status == "Accepted")
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

        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
    }

    private void AddAudit(Booking booking, int userId, string action) => _dbContext.VenueAuditLogs.Add(new VenueAuditLog
    {
        VenueId = booking.Court.VenueId,
        ActorId = userId,
        Action = action,
        Timestamp = DateTime.UtcNow
    });

    private void PublishBookingChanged(Booking booking, string action)
    {
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            booking.Court.VenueId,
            booking.CourtId,
            booking.StartTime,
            booking.EndTime,
            booking.Status,
            action));
        if (booking.MatchId.HasValue) _matchRealtime.Publish(booking.MatchId.Value, action);
    }

    private static string[] SplitPermissions(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static StaffBookingResponse MapBooking(Booking booking)
    {
        var operation = booking.Operation;
        var payment = booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault();
        var isMatchBooking = booking.MatchId.HasValue;
        var acceptedParticipants = booking.Match?.MatchParticipants
            .Where(item => item.Status == "Approved" || item.Status == "Accepted")
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
            PlayerName = booking.Player?.User.Username
                ?? acceptedParticipants.FirstOrDefault(item => item.IsHost)?.Player.User.Username
                ?? "Khach",
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
            NoShowAt = AsUtc(operation?.NoShowAt),
            CheckInGroups = booking.CheckInGroups.OrderBy(group => group.StartTime).Select(group => new StaffCheckInGroupResponse
            {
                BookingCheckInGroupId = group.BookingCheckInGroupId,
                CheckInCode = group.CheckInCode,
                CourtId = group.CourtId,
                CourtNumber = group.Court.CourtNumber,
                StartTime = group.StartTime,
                EndTime = group.EndTime,
                CheckInStatus = group.CheckInStatus,
                IsCheckInWindowOpen = localNow >= group.StartTime.AddMinutes(-30) && localNow <= group.EndTime,
                CanMarkNoShow = localNow >= group.StartTime.AddMinutes(15) && group.CheckInStatus is not ("CheckedIn" or "NoShow"),
                CodeVerifiedAt = AsUtc(group.CodeVerifiedAt),
                CheckedInAt = AsUtc(group.CheckedInAt),
                NoShowAt = AsUtc(group.NoShowAt)
            }).ToList()
        };
    }

    private static bool AreAllMatchPlayersPaid(Booking booking)
    {
        if (booking.Match is null) return false;

        var playerIds = booking.Match.MatchParticipants
            .Where(item => item.Status == "Approved" || item.Status == "Accepted")
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

    private static DateTime? AsUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}

public sealed record StaffOperationResult<T>(
    StaffOperationResultStatus Status,
    T? Value = default,
    string? ErrorMessage = null)
{
    public static StaffOperationResult<T> Success(T value) =>
        new(StaffOperationResultStatus.Success, value, ErrorMessage: null);

    public static StaffOperationResult<T> BadRequest(string errorMessage) =>
        new(StaffOperationResultStatus.BadRequest, Value: default, errorMessage);

    public static StaffOperationResult<T> Unauthorized() =>
        new(StaffOperationResultStatus.Unauthorized, Value: default, ErrorMessage: null);

    public static StaffOperationResult<T> NotFound(string errorMessage) =>
        new(StaffOperationResultStatus.NotFound, Value: default, errorMessage);

    public static StaffOperationResult<T> Conflict(string errorMessage) =>
        new(StaffOperationResultStatus.Conflict, Value: default, errorMessage);
}

public enum StaffOperationResultStatus
{
    Success,
    BadRequest,
    Unauthorized,
    NotFound,
    Conflict
}
