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
        var localNow = VietnamTime.Now;

        var booking = await OperationsBookingQuery()
            .SingleOrDefaultAsync(item => item.BookingCode == code, cancellationToken);

        if (booking is null && code.StartsWith("PL-", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(code[3..], out var parsedBookingId))
        {
            booking = await OperationsBookingQuery()
                .SingleOrDefaultAsync(item => item.BookingId == parsedBookingId, cancellationToken);
        }

        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Không tìm thấy đơn đặt sân với mã này.");

        var staff = await EnsurePermissionAsync(userId.Value, booking.Court.VenueId, "VerifyBooking", cancellationToken);
        if (staff is null)
            return StaffOperationResult<StaffBookingResponse>.Forbidden("Bạn không có quyền kiểm tra mã đặt sân tại cụm sân này.");

        if (booking.Status is "Cancelled" or "Expired")
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Đơn đặt sân đã bị hủy hoặc đã hết hạn.");

        if (booking.Status != "Confirmed")
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Đơn đặt sân chưa được xác nhận thanh toán.");

        var openWindowStart = booking.StartTime.AddMinutes(-30);
        if (localNow < openWindowStart)
            return StaffOperationResult<StaffBookingResponse>.BadRequest("Chỉ có thể quét mã trong vòng 30 phút trước giờ bắt đầu.");

        booking.Operation ??= new BookingOperation
        {
            BookingId = booking.BookingId,
            CheckInStatus = "NotCheckedIn"
        };

        var operation = booking.Operation;
        var wasUpdated = false;

        if (operation.CodeVerifiedAt is null)
        {
            operation.CodeVerifiedAt = DateTime.UtcNow;
            operation.CodeVerifiedByUserId = userId.Value;
            operation.UpdatedAt = DateTime.UtcNow;
            wasUpdated = true;
        }

        if (wasUpdated)
        {
            await _venueRepository.AddAuditLogAsync(NewAudit(booking.Court.VenueId, userId.Value, $"CodeVerified:{booking.BookingId}"), cancellationToken);
            await _bookingRepository.SaveChangesAsync(cancellationToken);
        }

        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking, localNow));
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

        var booking = await OperationsBookingQuery()
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Không tìm thấy đơn đặt sân.");

        var staff = await EnsurePermissionAsync(userId.Value, booking.Court.VenueId, "ConfirmPayment", cancellationToken);
        if (staff is null)
            return StaffOperationResult<StaffBookingResponse>.Forbidden("Bạn không có quyền xác nhận thanh toán tại cụm sân này.");

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

        var booking = await OperationsBookingQuery()
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Không tìm thấy đơn đặt sân.");

        var staff = await EnsurePermissionAsync(userId.Value, booking.Court.VenueId, "CheckIn", cancellationToken);
        if (staff is null)
            return StaffOperationResult<StaffBookingResponse>.Forbidden("Bạn không có quyền check-in tại cụm sân này.");

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

        var booking = await OperationsBookingQuery()
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Không tìm thấy đơn đặt sân.");

        var staff = await EnsurePermissionAsync(userId.Value, booking.Court.VenueId, "MarkNoShow", cancellationToken);
        if (staff is null)
            return StaffOperationResult<StaffBookingResponse>.Forbidden("Bạn không có quyền đánh dấu vắng mặt tại cụm sân này.");

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
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return StaffOperationResult<PaginatedResponse<StaffBookingResponse>>.Unauthorized();

        var assignedVenueIds = await _venueRepository.Staff.AsNoTracking()
            .Where(item => item.UserId == userId && item.IsActive)
            .Select(item => item.VenueId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (assignedVenueIds.Count == 0)
            return StaffOperationResult<PaginatedResponse<StaffBookingResponse>>.Success(
                Pagination.Create(new List<StaffBookingResponse>(), 0, page, pageSize));

        if (venueId.HasValue && !assignedVenueIds.Contains(venueId.Value))
            return StaffOperationResult<PaginatedResponse<StaffBookingResponse>>.Forbidden("Bạn không phải nhân viên của cụm sân này.");

        var query = OperationsBookingQuery()
            .Where(item => item.PlayerId != null && (venueId.HasValue
                ? item.Court.VenueId == venueId.Value
                : assignedVenueIds.Contains(item.Court.VenueId)));

        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);
            query = query.Where(item => item.StartTime >= start && item.StartTime < end);
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(item => item.Status == status);
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

        var booking = await OperationsBookingQuery()
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null)
            return StaffOperationResult<StaffBookingResponse>.NotFound("Không tìm thấy đơn đặt sân.");

        var staff = await EnsurePermissionAsync(userId.Value, booking.Court.VenueId, "ViewBookings", cancellationToken);
        if (staff is null)
            return StaffOperationResult<StaffBookingResponse>.Forbidden("Bạn không có quyền xem đơn đặt sân tại cụm sân này.");

        var localNow = VietnamTime.Now;
        return StaffOperationResult<StaffBookingResponse>.Success(MapBooking(booking, localNow));
    }

    // Interface forwarding methods
    public Task<StaffOperationResult<PaginatedResponse<StaffBookingResponse>>> ListTodayBookingsAsync(
        int? userId, DateOnly? date, string? bookingType, int? venueId, int page, int pageSize, CancellationToken cancellationToken) =>
        ListBookingsAsync(venueId, date, bookingType, null, page, pageSize, userId, cancellationToken);

    public Task<StaffOperationResult<StaffBookingResponse>> SearchBookingAsync(
        int? userId, string code, CancellationToken cancellationToken) =>
        VerifyCodeAsync(new StaffVerifyCodeRequest { Code = code }, userId, cancellationToken);

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

    private async Task<PicklinkBackend.Models.Staff?> EnsurePermissionAsync(
        int userId,
        int venueId,
        string requiredPermission,
        CancellationToken cancellationToken)
    {
        var staff = await _venueRepository.Staff.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId && item.VenueId == venueId && item.IsActive, cancellationToken);

        if (staff is null) return null;

        var permissions = staff.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return permissions.Contains(requiredPermission, StringComparer.OrdinalIgnoreCase) ? staff : null;
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
            .Include(item => item.Match)
            .Include(item => item.Court).ThenInclude(item => item.Venue);
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

    private static StaffBookingResponse MapBooking(Booking booking, DateTime localNow)
    {
        var payment = booking.Payments
            .OrderByDescending(item => item.Status == "WaitingForConfirmation")
            .ThenByDescending(item => item.Status == "Pending")
            .ThenByDescending(item => item.Status == "Paid")
            .ThenByDescending(item => item.SubmittedAt)
            .ThenByDescending(item => item.PaymentId)
            .FirstOrDefault();

        var occurrences = booking.CheckInGroups
            .Select(group => new BookingOccurrence(group.StartTime, group.EndTime, group.CheckInStatus))
            .ToList();

        var overallCheckInStatus = BookingOccurrencePolicy.GetCheckInStatus(
            booking.Status,
            booking.Operation?.CheckInStatus,
            occurrences,
            localNow,
            booking.StartTime,
            booking.EndTime);

        return new StaffBookingResponse
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode ?? $"PL-{booking.BookingId}",
            BookingStatus = booking.Status,
            CheckInStatus = overallCheckInStatus,
            PaymentStatus = payment?.Status ?? "Pending",
            PaymentMethod = payment?.PaymentMethod,
            PaymentId = payment?.PaymentId,
            TotalAmount = booking.TotalAmount,
            CourtAmount = booking.CourtAmount,
            HourlyPrice = booking.HourlyPriceSnapshot,
            VenueId = booking.Court.VenueId,
            VenueName = booking.Court.Venue.VenueName,
            CourtId = booking.CourtId,
            CourtNumber = booking.Court.CourtNumber,
            PlayerName = booking.Player?.User.Username ?? "Khach",
            PlayerEmail = booking.Player?.User.Email,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            CreatedAt = AsUtc(booking.CreatedAt),
            HoldExpiresAt = AsUtc(booking.HoldExpiresAt),
            CodeVerifiedAt = AsUtc(booking.Operation?.CodeVerifiedAt),
            PaymentConfirmedAt = AsUtc(booking.Operation?.PaymentConfirmedAt),
            CheckedInAt = AsUtc(booking.Operation?.CheckedInAt),
            NoShowAt = AsUtc(booking.Operation?.NoShowAt),
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
                CheckInStatus = group.CheckInStatus
            }).ToList()
        };
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
