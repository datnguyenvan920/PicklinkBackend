using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Matches;
using PicklinkBackend.Services.Payments;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;
using PicklinkBackend.Services.Staff;

namespace PicklinkBackend.Services.Staff.Implementations;

public sealed class StaffOperationService : IStaffOperationService
{
    private readonly IVenueRepository _venueRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly PaymentRealtimeNotifier _paymentRealtime;
    private readonly MatchRealtimeNotifier _matchRealtime;

    public StaffOperationService(
        IVenueRepository venueRepository,
        IBookingRepository bookingRepository,
        IPaymentRepository paymentRepository,
        ScheduleRealtimeNotifier scheduleRealtime,
        PaymentRealtimeNotifier paymentRealtime,
        MatchRealtimeNotifier matchRealtime)
    {
        _venueRepository = venueRepository;
        _bookingRepository = bookingRepository;
        _paymentRepository = paymentRepository;
        _scheduleRealtime = scheduleRealtime;
        _paymentRealtime = paymentRealtime;
        _matchRealtime = matchRealtime;
    }

    public async Task<StaffOperationResult<List<StaffAssignmentResponse>>> ListAssignmentsAsync(
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<List<StaffAssignmentResponse>>.Unauthorized();

        var assignments = await _venueRepository.Staff.AsNoTracking()
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
            Permissions = item.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        }).ToList());
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> VerifyCodeAsync(
        StaffVerifyCodeRequest request,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        var code = request.Code.Trim().ToUpperInvariant();
        if (code.Length < 3)
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Vui lòng nhập mã check-in.");
        var isCompactPersonalCode = code.Length == CheckInCode.Length;
        if (code.StartsWith("PL-", StringComparison.OrdinalIgnoreCase))
            return StaffOperationResult<StaffBookingResponse>.BadRequest(
                "Mã booking chỉ dùng để tra cứu thông tin. Vui lòng quét mã check-in để check-in.");

        await using var transaction = await _bookingRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"staff-code:{code}", cancellationToken))
            return StaffOperationResult<StaffBookingResponse>.Conflict("Mã đang được xử lý. Vui lòng thử lại.");

        var matchingBookings = await ScopedBookings(userId.Value, "VerifyBooking", "CheckIn")
            .Where(item =>
                item.CheckInGroups.Any(group => group.CheckInCode == code)
                || item.MatchId != null && item.Payments.Any(payment =>
                    payment.Status == "Paid"
                    && payment.TransferCode != null
                    && (payment.TransferCode == code
                        || isCompactPersonalCode && payment.TransferCode.EndsWith(code))))
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matchingBookings.Count == 0)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Không tìm thấy mã check-in tại cụm sân được phép quản lý.");
        if (matchingBookings.Count > 1)
            return StaffOperationResult<StaffBookingResponse>.Conflict("Mã check-in bị trùng. Vui lòng liên hệ quản trị viên.");

        var booking = matchingBookings[0];
        if (booking.Status != "Confirmed")
            return StaffOperationResult<StaffBookingResponse>.Conflict("Chỉ check-in cho booking đã xác nhận.");

        if (!await SqlServerBookingLock.AcquireAsync(
                transaction, $"staff-checkin:{booking.BookingId}", cancellationToken))
            return StaffOperationResult<StaffBookingResponse>.Conflict("Booking đang được xử lý. Vui lòng thử lại.");

        var localNow = VietnamTime.Now;
        var now = DateTime.UtcNow;
        var group = booking.CheckInGroups.SingleOrDefault(item => item.CheckInCode == code);

        if (group is not null)
        {
            if (booking.MatchId.HasValue)
                return StaffOperationResult<StaffBookingResponse>.BadRequest("Đơn ghép trận phải quét mã cá nhân của người chơi.");

            if (!booking.Payments.Any(item => item.Status == "Paid"))
                return StaffOperationResult<StaffBookingResponse>.Conflict("Booking chưa được xác nhận thanh toán.");

            if (group.CheckInStatus == "CheckedIn")
            {
                await transaction.CommitAsync(cancellationToken);
                return StaffOperationResult<StaffBookingResponse>.Success(
                    MapBooking(booking, verifiedCheckInGroupId: group.BookingCheckInGroupId));
            }

            if (group.CheckInStatus == "NoShow")
                return StaffOperationResult<StaffBookingResponse>.Conflict("Khung giờ đã được đánh dấu vắng mặt.");

            if (localNow < group.StartTime.AddMinutes(-30) || localNow > group.EndTime)
                return StaffOperationResult<StaffBookingResponse>.Conflict("Ngoài thời gian check-in.");

            group.CodeVerifiedAt = now;
            group.CodeVerifiedByUserId = userId.Value;
            group.CheckInStatus = "CheckedIn";
            group.CheckedInAt = now;
            group.CheckedInByUserId = userId.Value;
            group.UpdatedAt = now;
            SyncBookingCheckInStatusFromGroups(booking, userId.Value, now);

            await _venueRepository.AddAuditLogAsync(
                NewAudit(booking.Court.VenueId, userId.Value, $"CheckInGroupScanned:{booking.BookingId}:{group.BookingCheckInGroupId}"),
                cancellationToken);
            await _bookingRepository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            PublishScheduleUpdate(booking, "CheckInGroupCheckedIn");

            return StaffOperationResult<StaffBookingResponse>.Success(
                MapBooking(booking, verifiedCheckInGroupId: group.BookingCheckInGroupId));
        }

        var payerPlayerIds = booking.Payments
            .Where(item => item.Status == "Paid"
                && item.TransferCode != null
                && (item.TransferCode == code
                    || isCompactPersonalCode && item.TransferCode.EndsWith(code)))
            .Select(item => item.PayerId)
            .Distinct()
            .Take(2)
            .ToList();

        if (booking.MatchId is null || booking.Match is null || payerPlayerIds.Count == 0)
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Mã check-in không hợp lệ.");
        if (payerPlayerIds.Count > 1)
            return StaffOperationResult<StaffBookingResponse>.Conflict("Mã check-in bị trùng. Vui lòng liên hệ quản trị viên.");

        var payerPlayerId = payerPlayerIds[0];

        // One check-in code covers one round on one court over adjacent slots, so a personal scan
        // is recorded against the code whose window is open rather than against the whole match.
        var scannedGroup = booking.CheckInGroups
            .Where(item => localNow >= item.StartTime.AddMinutes(-30) && localNow <= item.EndTime)
            .OrderBy(item => item.StartTime)
            .ThenBy(item => item.CourtId)
            .FirstOrDefault();
        if (scannedGroup is null)
            return StaffOperationResult<StaffBookingResponse>.Conflict("Ngoài thời gian check-in.");

        // A replacement never gets their own payment/code for this booking — they check in with the
        // absent player's code instead — so if someone was approved to replace `payerPlayerId` for
        // this specific slot, THEY are the one physically at the court, not the payer.
        var approvedReplacementPlayerId = booking.Match.SlotAbsences
            .Where(absence => absence.BookingCheckInGroupId == scannedGroup.BookingCheckInGroupId
                && absence.UnavailablePlayerId == payerPlayerId
                && absence.Status == "Filled")
            .SelectMany(absence => absence.ReplacementRequests)
            .Where(request => request.Status == "Approved")
            .Select(request => (int?)request.PlayerId)
            .FirstOrDefault();
        int? verifiedPlayerId = approvedReplacementPlayerId ?? payerPlayerId;

        if (approvedReplacementPlayerId is null)
        {
            var participant = booking.Match.MatchParticipants.SingleOrDefault(item =>
                item.PlayerId == verifiedPlayerId.Value && item.Status is "Approved" or "Accepted");
            if (participant is null)
                return StaffOperationResult<StaffBookingResponse>.NotFound("Người chơi không thuộc nhóm đã được chấp nhận.");
        }

        var existingAttendance = booking.Match.MatchCheckIns
            .FirstOrDefault(item => item.PlayerId == verifiedPlayerId.Value
                && item.BookingCheckInGroupId == scannedGroup.BookingCheckInGroupId);
        if (existingAttendance?.Status == "Present")
        {
            await transaction.CommitAsync(cancellationToken);
            return StaffOperationResult<StaffBookingResponse>.Success(
                MapBooking(booking, verifiedPlayerId: verifiedPlayerId));
        }

        if (existingAttendance is not null)
            return StaffOperationResult<StaffBookingResponse>.Conflict("Người chơi đã được đánh dấu vắng mặt.");

        var staffId = await _venueRepository.Staff.AsNoTracking()
            .Where(item => item.UserId == userId.Value
                && item.VenueId == booking.Court.VenueId
                && item.IsActive)
            .Select(item => (int?)item.StaffId)
            .FirstOrDefaultAsync(cancellationToken);

        booking.Match.MatchCheckIns.Add(new MatchCheckIn
        {
            MatchId = booking.MatchId.Value,
            PlayerId = verifiedPlayerId.Value,
            BookingCheckInGroupId = scannedGroup.BookingCheckInGroupId,
            StaffId = staffId,
            Status = "Present",
            CheckedInAt = now
        });

        var acceptedPlayerIds = booking.Match.MatchParticipants
            .Where(item => item.Status is "Approved" or "Accepted")
            .Select(item => item.PlayerId)
            .ToHashSet();
        var groupIds = booking.CheckInGroups.Select(item => item.BookingCheckInGroupId).ToHashSet();
        // A replacement's attendance is recorded under their own PlayerId (see above), but they stand
        // in for whichever roster member they replaced — map it back to that original player so the
        // round's completion count isn't permanently short one player it can never satisfy (the
        // replacement is never itself in acceptedPlayerIds).
        var replacedByPlayerId = booking.Match.SlotAbsences
            .Where(absence => groupIds.Contains(absence.BookingCheckInGroupId) && absence.Status == "Filled")
            .SelectMany(absence => absence.ReplacementRequests
                .Where(request => request.Status == "Approved")
                .Select(request => new { request.PlayerId, absence.UnavailablePlayerId }))
            .GroupBy(pair => pair.PlayerId)
            .ToDictionary(group => group.Key, group => group.First().UnavailablePlayerId);
        var operation = EnsureOperation(booking);
        // Only attendance for this round counts, otherwise a later round would look fully
        // checked in the moment it is created.
        var processedAttendances = booking.Match.MatchCheckIns
            .Where(item => item.BookingCheckInGroupId.HasValue
                && groupIds.Contains(item.BookingCheckInGroupId.Value))
            .Select(item => replacedByPlayerId.GetValueOrDefault(item.PlayerId, item.PlayerId))
            .Where(acceptedPlayerIds.Contains)
            .Distinct()
            .ToList();
        operation.CheckInStatus = processedAttendances.Count == acceptedPlayerIds.Count
            ? "CheckedIn"
            : "Ready";
        if (operation.CheckInStatus == "CheckedIn")
        {
            operation.CheckedInAt = now;
            operation.CheckedInByUserId = userId.Value;
        }
        operation.UpdatedAt = now;

        await _venueRepository.AddAuditLogAsync(
            NewAudit(booking.Court.VenueId, userId.Value, $"MatchPlayerScanned:{booking.BookingId}:{verifiedPlayerId.Value}"),
            cancellationToken);
        await _bookingRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(booking.MatchId.Value, "PlayerCheckedIn");
        PublishScheduleUpdate(booking, "MatchPlayerCheckedIn");

        return StaffOperationResult<StaffBookingResponse>.Success(
            MapBooking(booking, verifiedPlayerId: verifiedPlayerId));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> ConfirmPaymentAsync(
        int bookingId,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        await using var transaction = await _bookingRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{bookingId}", cancellationToken))
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Đơn đặt sân đang được xử lý. Vui lòng thử lại.");

        var booking = await ScopedBookings(userId.Value, "ConfirmPayment")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Không tìm thấy đơn đặt sân thuộc cụm sân được phép quản lý.");

        var primaryPayment = booking.Payments
            .OrderByDescending(payment => payment.Status == "WaitingForConfirmation")
            .ThenByDescending(payment => payment.SubmittedAt)
            .ThenByDescending(payment => payment.PaymentId)
            .FirstOrDefault();

        if (primaryPayment is null)
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Đơn đặt sân không có thông tin thanh toán.");

        var localNow = VietnamTime.Now;
        var now = DateTime.UtcNow;

        if (primaryPayment.Status != "Paid")
        {
            primaryPayment.Status = "Paid";
            primaryPayment.PaidAt = now;
            primaryPayment.VerifiedAt = now;
            primaryPayment.VerifiedByUserId = userId.Value;
            primaryPayment.RejectionReason = null;
            primaryPayment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = primaryPayment.Status,
                ToStatus = "Paid",
                Action = "StaffConfirmed",
                Reason = "Nhân viên thu ngân đã xác nhận thanh toán tại sân.",
                ActorUserId = userId.Value,
                CreatedAt = now
            });
        }

        booking.Status = "Confirmed";
        booking.HoldExpiresAt = null;
        booking.HoldRemainingSeconds = null;

        booking.Operation ??= new BookingOperation
        {
            BookingId = booking.BookingId,
            CheckInStatus = "NotCheckedIn"
        };

        var operation = booking.Operation;
        if (operation.PaymentConfirmedAt is null)
        {
            operation.PaymentConfirmedAt = now;
            operation.PaymentConfirmedByUserId = userId.Value;
        }

        operation.UpdatedAt = now;

        await _venueRepository.AddAuditLogAsync(NewAudit(booking.Court.VenueId, userId.Value, $"StaffPaymentConfirmed:{booking.BookingId}"), cancellationToken);
        await _bookingRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _paymentRealtime.Publish(new PaymentChangedEvent(
            primaryPayment.PaymentId,
            booking.BookingId,
            booking.Court.VenueId,
            primaryPayment.Status,
            "StaffConfirmed"));

        if (booking.MatchId.HasValue)
        {
            _matchRealtime.Publish(booking.MatchId.Value, "PaymentConfirmed");
        }

        PublishScheduleUpdate(booking, "StaffPaymentConfirmed");

        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking, localNow));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> CheckInAsync(
        int? userId,
        int bookingId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        var booking = await ScopedBookings(userId.Value, "CheckIn")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Không tìm thấy đơn đặt sân thuộc cụm sân được phép quản lý.");

        var localNow = VietnamTime.Now;
        if (booking.Status != "Confirmed")
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Đơn đặt sân chưa được xác nhận hoặc đã bị hủy.");

        var openWindowStart = booking.StartTime.AddMinutes(-30);
        if (localNow < openWindowStart)
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Chỉ có thể check-in trong vòng 30 phút trước giờ bắt đầu.");

        booking.Operation ??= new BookingOperation
        {
            BookingId = booking.BookingId,
            CheckInStatus = "NotCheckedIn"
        };

        var operation = booking.Operation;
        if (operation.CheckInStatus == "NoShow")
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Đơn đặt sân đã được đánh dấu Vắng mặt.");

        var now = DateTime.UtcNow;
        var activeOccurrence = ResolveActiveOccurrence(booking, localNow);

        if (activeOccurrence is not null)
        {
            var targetGroup = booking.CheckInGroups.FirstOrDefault(group =>
                group.StartTime == activeOccurrence.Value.StartTime && group.EndTime == activeOccurrence.Value.EndTime);

            if (targetGroup is null)
            {
                targetGroup = new BookingCheckInGroup
                {
                    BookingId = booking.BookingId,
                    CourtId = booking.CourtId,
                    StartTime = activeOccurrence.Value.StartTime,
                    EndTime = activeOccurrence.Value.EndTime,
                    // checkInCode is unique-indexed, so the default empty string would collide with
                    // the next booking that reaches this path.
                    CheckInCode = CheckInCode.Next(),
                    CheckInStatus = "NotCheckedIn"
                };
                booking.CheckInGroups.Add(targetGroup);
            }

            targetGroup.CheckInStatus = "CheckedIn";
        }

        operation.CheckInStatus = "CheckedIn";
        operation.CheckedInAt ??= now;
        operation.CheckedInByUserId ??= userId.Value;
        operation.UpdatedAt = now;

        await _venueRepository.AddAuditLogAsync(NewAudit(booking.Court.VenueId, userId.Value, $"CheckedIn:{booking.BookingId}"), cancellationToken);
        await _bookingRepository.SaveChangesAsync(cancellationToken);

        PublishScheduleUpdate(booking, "CheckedIn");

        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking, localNow));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> MarkNoShowAsync(
        int? userId,
        int bookingId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        var booking = await ScopedBookings(userId.Value, "MarkNoShow")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Không tìm thấy đơn đặt sân thuộc cụm sân được phép quản lý.");

        var localNow = VietnamTime.Now;
        if (booking.Status != "Confirmed")
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Chỉ có thể đánh dấu vắng mặt cho đơn đã xác nhận.");

        if (localNow <= booking.EndTime)
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Chỉ có thể đánh dấu vắng mặt sau khi hết giờ đặt.");

        booking.Operation ??= new BookingOperation
        {
            BookingId = booking.BookingId,
            CheckInStatus = "NotCheckedIn"
        };

        var operation = booking.Operation;
        if (operation.CheckInStatus == "CheckedIn")
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Không thể đánh dấu vắng mặt đơn đặt sân đã check-in.");

        var now = DateTime.UtcNow;
        operation.CheckInStatus = "NoShow";
        operation.NoShowAt ??= now;
        operation.NoShowByUserId ??= userId.Value;
        operation.UpdatedAt = now;

        foreach (var group in booking.CheckInGroups)
        {
            if (group.CheckInStatus != "CheckedIn") group.CheckInStatus = "NoShow";
        }

        await _venueRepository.AddAuditLogAsync(NewAudit(booking.Court.VenueId, userId.Value, $"MarkedNoShow:{booking.BookingId}"), cancellationToken);
        await _bookingRepository.SaveChangesAsync(cancellationToken);

        PublishScheduleUpdate(booking, "NoShow");

        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking, localNow));
    }

    public async Task<StaffOperationResult<PaginatedResponse<StaffBookingResponse>>> ListBookingsAsync(
        int? venueId,
        DateOnly? date,
        string? status,
        string? search,
        int page,
        int pageSize,
        bool confirmedOnly,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<PaginatedResponse<StaffBookingResponse>>.Unauthorized();

        const string viewPermission = ",ViewBookings,";
        var accessibleVenueIds = await _venueRepository.Venues.AsNoTracking()
            .Where(item => item.Owner.UserId == userId.Value
                || item.Staff.Any(staff =>
                    staff.UserId == userId.Value
                    && staff.IsActive
                    && ("," + staff.Permissions + ",").Contains(viewPermission)))
            .Select(item => item.VenueId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (accessibleVenueIds.Count == 0)
            return StaffOperationResult<PaginatedResponse<StaffBookingResponse>>.Success(
                Pagination.Create(new List<StaffBookingResponse>(), 0, page, pageSize));

        if (venueId.HasValue && !accessibleVenueIds.Contains(venueId.Value))
            return StaffOperationResult<PaginatedResponse<StaffBookingResponse>>.Forbidden("Bạn không có quyền quản lý cụm sân này.");

        var query = OperationsBookingQuery()
            .Where(item => item.TicketSession == null && (venueId.HasValue
                ? item.Court.VenueId == venueId.Value
                : accessibleVenueIds.Contains(item.Court.VenueId)));

        if (confirmedOnly)
            query = query.Where(item => item.Status == "Confirmed");

        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);
            query = query.Where(item => item.StartTime >= start && item.StartTime < end);
        }

        if (status?.Equals("Court", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = query.Where(item => item.MatchId == null);
        }
        else if (status?.Equals("Match", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = query.Where(item => item.MatchId != null);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(item =>
                (item.BookingCode != null && item.BookingCode.Contains(keyword)) ||
                item.Player!.User.Username.Contains(keyword));
        }

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);

        var totalCount = await query.CountAsync(cancellationToken);
        var bookings = await query
            .OrderByDescending(item => item.StartTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var localNow = VietnamTime.Now;
        return StaffOperationResult<PaginatedResponse<StaffBookingResponse>>.Success(
            Pagination.Create(bookings.Select(item => MapBooking(item, localNow)).ToList(), totalCount, page, pageSize));
    }

    public async Task<StaffOperationResult<StaffBookingResponse>> GetBookingAsync(
        int? userId,
        int bookingId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        var booking = await ScopedBookings(userId.Value, "ViewBookings")
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Không tìm thấy đơn đặt sân thuộc cụm sân được phép quản lý.");

        var localNow = VietnamTime.Now;
        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking, localNow));
    }

    // Interface forwarding methods
    public Task<StaffOperationResult<PaginatedResponse<StaffBookingResponse>>> ListTodayBookingsAsync(
        int? userId, DateOnly? date, string? bookingType, int? venueId, int page, int pageSize, CancellationToken cancellationToken) =>
        ListBookingsAsync(venueId, date, bookingType, null, page, pageSize, false, userId, cancellationToken);

    public Task<StaffOperationResult<PaginatedResponse<StaffBookingResponse>>> ListConfirmedTodayBookingsAsync(
        int? userId, DateOnly? date, string? bookingType, int? venueId, int page, int pageSize, CancellationToken cancellationToken) =>
        ListBookingsAsync(venueId, date, bookingType, null, page, pageSize, true, userId, cancellationToken);

    public async Task<StaffOperationResult<StaffBookingResponse>> SearchBookingAsync(
        int? userId, string code, CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<StaffBookingResponse>.Unauthorized();

        var normalizedCode = code.Trim().ToUpperInvariant();
        if (normalizedCode.Length < 3)
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Vui lòng nhập mã booking.");

        var booking = await ScopedBookings(userId.Value, "ViewBookings")
            .AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.Status == "Confirmed" && item.BookingCode == normalizedCode,
                cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound(
                "Không tìm thấy mã booking đã xác nhận tại cụm sân được phép quản lý.");

        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking));
    }

    public Task<StaffOperationResult<StaffBookingResponse>> VerifyBookingCodeByCodeAsync(
        int? userId, VerifyBookingCodeRequest request, CancellationToken cancellationToken) =>
        VerifyCodeAsync(new StaffVerifyCodeRequest { Code = request.Code }, userId, cancellationToken);

    public Task<StaffOperationResult<StaffBookingResponse>> CheckInGroupAsync(
        int? userId, int bookingId, int checkInGroupId, CancellationToken cancellationToken) =>
        CheckInAsync(userId, bookingId, cancellationToken);

    public Task<StaffOperationResult<StaffBookingResponse>> MarkGroupNoShowAsync(
        int? userId, int bookingId, int checkInGroupId, CancellationToken cancellationToken) =>
        MarkNoShowAsync(userId, bookingId, cancellationToken);

    public Task<StaffOperationResult<StaffBookingResponse>> ConfirmAtCourtPaymentAsync(
        int? userId, int bookingId, CancellationToken cancellationToken) =>
        ConfirmPaymentAsync(bookingId, userId, cancellationToken);

    public Task<StaffOperationResult<StaffBookingResponse>> CheckInMatchParticipantAsync(
        int? userId, int bookingId, int playerId, CancellationToken cancellationToken) =>
        CheckInAsync(userId, bookingId, cancellationToken);

    public Task<StaffOperationResult<StaffBookingResponse>> MarkMatchParticipantNoShowAsync(
        int? userId, int bookingId, int playerId, CancellationToken cancellationToken) =>
        MarkNoShowAsync(userId, bookingId, cancellationToken);

    public Task<StaffOperationResult<List<StaffNotificationResponse>>> ListNotificationsAsync(
        int? userId, CancellationToken cancellationToken) =>
        Task.FromResult(StaffOperationResult<List<StaffNotificationResponse>>.Success(new List<StaffNotificationResponse>()));

    private IQueryable<Booking> ScopedBookings(
        int userId,
        string permission,
        string? alternatePermission = null)
    {
        var permissionToken = $",{permission},";
        var alternatePermissionToken = alternatePermission is null ? null : $",{alternatePermission},";

        return OperationsBookingQuery().Where(item =>
            item.TicketSession == null
            && (item.Court.Venue.Owner.UserId == userId
                || item.Court.Venue.Staff.Any(staff =>
                    staff.UserId == userId
                    && staff.IsActive
                    && (("," + staff.Permissions + ",").Contains(permissionToken)
                        || alternatePermissionToken != null
                        && ("," + staff.Permissions + ",").Contains(alternatePermissionToken)))));
    }

    private IQueryable<Booking> OperationsBookingQuery()
    {
        return _bookingRepository.Bookings
            .AsSplitQuery()
            .Include(item => item.Operation)
            .Include(item => item.Slots).ThenInclude(slot => slot.Court)
            .Include(item => item.CheckInGroups).ThenInclude(group => group.Court)
            .Include(item => item.Payments)
            .Include(item => item.Player).ThenInclude(item => item!.User)
            .Include(item => item.Match).ThenInclude(item => item!.MatchParticipants)
                .ThenInclude(item => item.Player).ThenInclude(item => item.User)
            .Include(item => item.Match).ThenInclude(item => item!.MatchCheckIns)
            .Include(item => item.Match).ThenInclude(item => item!.SlotAbsences)
                .ThenInclude(absence => absence.ReplacementRequests)
            .Include(item => item.Court).ThenInclude(item => item.Venue)
                .ThenInclude(item => item.Owner);
    }

    private static BookingOccurrence? ResolveActiveOccurrence(Booking booking, DateTime localNow)
    {
        var occurrences = booking.CheckInGroups
            .Select(group => new BookingOccurrence(group.StartTime, group.EndTime, group.CheckInStatus))
            .ToList();

        if (occurrences.Count == 0)
        {
            occurrences.Add(new BookingOccurrence(booking.StartTime, booking.EndTime, booking.Operation?.CheckInStatus ?? "NotCheckedIn"));
        }

        return occurrences.FirstOrDefault(occurrence => localNow >= occurrence.StartTime.AddMinutes(-30) && localNow <= occurrence.EndTime);
    }

    private static StaffBookingResponse MapBooking(
        Booking booking,
        DateTime? localNowOverride = null,
        int? verifiedPlayerId = null,
        int? verifiedCheckInGroupId = null)
    {
        var operation = booking.Operation;
        var venue = booking.Court.Venue;
        var payment = booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault();
        var isMatchBooking = booking.MatchId.HasValue;
        var acceptedParticipants = booking.Match?.MatchParticipants
            .Where(item => item.Status is "Approved" or "Accepted")
            .ToList();
        acceptedParticipants ??= [];
        // Attendance is per check-in code, so this round only counts the scans made against it.
        var bookingGroupIds = booking.CheckInGroups.Select(item => item.BookingCheckInGroupId).ToHashSet();
        var matchAttendances = booking.Match?.MatchCheckIns
            .Where(item => item.BookingCheckInGroupId.HasValue
                && bookingGroupIds.Contains(item.BookingCheckInGroupId.Value))
            .GroupBy(item => item.PlayerId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.CheckedInAt).First())
            ?? [];
        var localNow = localNowOverride ?? VietnamTime.Now;
        var groups = booking.CheckInGroups.OrderBy(group => group.StartTime).ToList();
        var startTime = groups.Count > 0 ? groups.Min(group => group.StartTime) : booking.StartTime;
        var endTime = groups.Count > 0 ? groups.Max(group => group.EndTime) : booking.EndTime;
        var checkInStatus = BookingOccurrencePolicy.GetCheckInStatus(
            booking.Status,
            operation?.CheckInStatus,
            groups.Select(group => new BookingOccurrence(group.StartTime, group.EndTime, group.CheckInStatus)),
            localNow,
            startTime,
            endTime);

        return new StaffBookingResponse
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode ?? $"PL-{booking.BookingId}",
            BookingType = isMatchBooking ? "Match" : "Court",
            MatchId = booking.MatchId,
            VerifiedPlayerId = verifiedPlayerId,
            VerifiedCheckInGroupId = verifiedCheckInGroupId,
            BookingStatus = booking.Status,
            CheckInStatus = checkInStatus,
            PaymentStatus = isMatchBooking ? GetMatchPaymentStatus(booking) : payment?.Status ?? "Pending",
            PaymentMethod = isMatchBooking ? "GroupOnline" : payment?.PaymentMethod,
            PaymentId = payment?.PaymentId,
            TotalAmount = booking.TotalAmount,
            CourtAmount = booking.CourtAmount,
            HourlyPrice = booking.HourlyPriceSnapshot,
            VenueId = venue.VenueId,
            VenueName = venue.VenueName,
            Address = venue.Address,
            CourtId = groups.FirstOrDefault()?.CourtId ?? booking.CourtId,
            CourtNumber = groups.FirstOrDefault()?.Court.CourtNumber ?? booking.Court.CourtNumber,
            PlayerName = booking.Player?.User.Username
                ?? acceptedParticipants.FirstOrDefault(item => item.IsHost)?.Player.User.Username
                ?? "Khách",
            PlayerEmail = booking.Player?.User.Email,
            ParticipantCount = isMatchBooking ? acceptedParticipants.Count : 1,
            CheckedInParticipantCount = isMatchBooking
                ? matchAttendances.Values.Count(item => item.Status == "Present")
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
            StartTime = startTime,
            EndTime = endTime,
            CreatedAt = AsUtc(booking.CreatedAt),
            HoldExpiresAt = AsUtc(booking.HoldExpiresAt),
            IsCheckInWindowOpen = groups.Count > 0
                ? groups.Any(group => localNow >= group.StartTime.AddMinutes(-30) && localNow <= group.EndTime)
                : localNow >= startTime.AddMinutes(-30) && localNow <= endTime,
            CanMarkNoShow = groups.Count > 0
                ? groups.Any(group => localNow >= group.StartTime.AddMinutes(15)
                    && group.CheckInStatus is not ("CheckedIn" or "NoShow"))
                : localNow >= startTime.AddMinutes(15) && checkInStatus is not ("CheckedIn" or "NoShow"),
            CodeVerifiedAt = AsUtc(operation?.CodeVerifiedAt),
            PaymentConfirmedAt = AsUtc(operation?.PaymentConfirmedAt),
            CheckedInAt = AsUtc(operation?.CheckedInAt),
            NoShowAt = AsUtc(operation?.NoShowAt),
            Slots = booking.Slots.OrderBy(slot => slot.StartTime).ThenBy(slot => slot.CourtId).Select(slot => new StaffBookingSlotResponse
            {
                BookingSlotId = slot.BookingSlotId,
                CourtId = slot.CourtId,
                CourtNumber = slot.Court.CourtNumber,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                CourtAmount = slot.CourtAmount
            }).ToList(),
            CheckInGroups = booking.CheckInGroups.OrderBy(group => group.StartTime).Select(group => new StaffCheckInGroupResponse
            {
                BookingCheckInGroupId = group.BookingCheckInGroupId,
                CourtId = group.CourtId,
                CourtNumber = group.Court.CourtNumber,
                StartTime = group.StartTime,
                EndTime = group.EndTime,
                CheckInStatus = group.CheckInStatus,
                IsCheckInWindowOpen = localNow >= group.StartTime.AddMinutes(-30) && localNow <= group.EndTime,
                CanMarkNoShow = localNow >= group.StartTime.AddMinutes(15)
                    && group.CheckInStatus is not ("CheckedIn" or "NoShow"),
                CodeVerifiedAt = AsUtc(group.CodeVerifiedAt),
                CheckedInAt = AsUtc(group.CheckedInAt),
                NoShowAt = AsUtc(group.NoShowAt)
            }).ToList()
        };
    }

    private static BookingOperation EnsureOperation(Booking booking)
    {
        if (booking.Operation is not null) return booking.Operation;

        booking.Operation = new BookingOperation
        {
            BookingId = booking.BookingId,
            CheckInStatus = "Ready",
            UpdatedAt = DateTime.UtcNow
        };
        return booking.Operation;
    }

    private static void SyncBookingCheckInStatusFromGroups(Booking booking, int actorId, DateTime now)
    {
        var operation = EnsureOperation(booking);
        var groups = booking.CheckInGroups.ToList();
        operation.CheckInStatus = groups.Count > 0 && groups.All(group => group.CheckInStatus == "CheckedIn")
            ? "CheckedIn"
            : "Ready";
        operation.CodeVerifiedAt ??= now;
        operation.CodeVerifiedByUserId ??= actorId;
        if (operation.CheckInStatus == "CheckedIn")
        {
            operation.CheckedInAt ??= now;
            operation.CheckedInByUserId ??= actorId;
        }
        operation.UpdatedAt = now;
    }

    private static bool AreAllMatchPlayersPaid(Booking booking)
    {
        if (booking.Match is null) return false;

        var playerIds = booking.Match.MatchParticipants
            .Where(item => item.Status is "Approved" or "Accepted")
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

    private void PublishScheduleUpdate(Booking booking, string action)
    {
        if (booking.Slots.Any())
        {
            foreach (var slot in booking.Slots)
            {
                _scheduleRealtime.Publish(new ScheduleChangedEvent(
                    slot.Court.VenueId,
                    slot.CourtId,
                    slot.StartTime,
                    slot.EndTime,
                    booking.Status,
                    action));
            }
        }
        else
        {
            _scheduleRealtime.Publish(new ScheduleChangedEvent(
                booking.Court.VenueId,
                booking.CourtId,
                booking.StartTime,
                booking.EndTime,
                booking.Status,
                action));
        }
    }

    private static VenueAuditLog NewAudit(int venueId, int actorId, string action) => new()
    {
        VenueId = venueId,
        ActorId = actorId,
        Action = action,
        Timestamp = DateTime.UtcNow
    };

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
