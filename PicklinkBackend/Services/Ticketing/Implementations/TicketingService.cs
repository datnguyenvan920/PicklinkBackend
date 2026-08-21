using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Bookings.Implementations;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Notifications.Implementations;
using PicklinkBackend.Services.Payments;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;
using PicklinkBackend.Services.Ticketing;

namespace PicklinkBackend.Services.Ticketing.Implementations;

public sealed partial class TicketingService : ITicketingService
{
    private const int MaximumAdvanceBookingMonths = 1;

    private readonly IPaymentRepository _paymentRepository;
    private readonly IConfiguration _configuration;
    private readonly PlayerScheduleConflictService _playerScheduleConflict;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly PaymentRealtimeNotifier _paymentRealtime;
    private readonly NotificationService _notifications;
    private readonly SePayReconciliationService _sePayReconciliation;

    public TicketingService(
        IPaymentRepository paymentRepository,
        IConfiguration configuration,
        PlayerScheduleConflictService playerScheduleConflict,
        ScheduleRealtimeNotifier scheduleRealtime,
        PaymentRealtimeNotifier paymentRealtime,
        NotificationService notifications,
        SePayReconciliationService sePayReconciliation)
    {
        _paymentRepository = paymentRepository;
        _configuration = configuration;
        _playerScheduleConflict = playerScheduleConflict;
        _scheduleRealtime = scheduleRealtime;
        _paymentRealtime = paymentRealtime;
        _notifications = notifications;
        _sePayReconciliation = sePayReconciliation;
    }

    public async Task<ServiceResult<PaginatedResponse<TicketSessionResponse>>> GetPublishedSessions(
        string? search,
        int? venueId,
        DateOnly? date,
        int? skillLevel,
        string? playFormat,
        decimal? minPrice,
        decimal? maxPrice,
        bool onlyAvailable,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var localNow = VietnamTime.Now;
        var query = _paymentRepository.TicketSessions.AsNoTracking()
            .AsSingleQuery()
            .Include(session => session.Booking).ThenInclude(booking => booking.Court).ThenInclude(court => court.Venue)
            .Include(session => session.Tickets).ThenInclude(ticket => ticket.Payment)
            .Where(session => session.Status == "Published"
                && session.Booking.Status == "Confirmed"
                && session.Booking.StartTime > localNow);

        if (venueId.HasValue) query = query.Where(session => session.Booking.Court.VenueId == venueId.Value);
        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);
            query = query.Where(session => session.Booking.StartTime >= start && session.Booking.StartTime < end);
        }
        if (!string.IsNullOrWhiteSpace(playFormat))
            query = query.Where(session => session.PlayFormat == playFormat);
        if (minPrice.HasValue)
            query = query.Where(session => session.TicketPrice >= minPrice.Value);
        if (maxPrice.HasValue)
            query = query.Where(session => session.TicketPrice <= maxPrice.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(session =>
                session.Title.Contains(keyword)
                || session.Booking.Court.Venue.VenueName.Contains(keyword)
                || session.Booking.Court.Venue.Address.Contains(keyword));
        }

        if (onlyAvailable)
        {
            query = query.Where(session => session.MaxPlayers > session.Tickets.Count(ticket =>
                ticket.Payment.Status == "WaitingForConfirmation"
                || ticket.Status == "Paid"
                || ticket.Status == "CheckedIn"
                || ticket.Status == "PendingPayment" && ticket.HoldExpiresAt > utcNow));
        }

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);

        if (!skillLevel.HasValue)
        {
            var sqlTotalCount = await query.CountAsync(cancellationToken);
            var sessions = await query
                .OrderBy(session => session.Booking.StartTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return Ok(Pagination.Create(
                sessions.Select(session => MapSession(session, utcNow, localNow)).ToList(),
                sqlTotalCount,
                page,
                pageSize));
        }

        // Skill ranges are stored as text, so preserve the existing parser while still
        // pushing every translatable filter (including availability) down to SQL.
        var skillFilteredSessions = (await query
                .OrderBy(session => session.Booking.StartTime)
                .ToListAsync(cancellationToken))
            .Where(session => TicketingPolicy.AllowsSkillLevel(session.SkillLevel, skillLevel.Value))
            .ToList();
        var totalCount = skillFilteredSessions.Count;
        var pagedItems = skillFilteredSessions
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(session => MapSession(session, utcNow, localNow))
            .ToList();

        return Ok(Pagination.Create(pagedItems, totalCount, page, pageSize));
    }

    public async Task<ServiceResult<TicketSessionResponse>> GetPublishedSession(
        int ticketSessionId,
        CancellationToken cancellationToken)
    {
        var session = await LoadSessionReadAsync(ticketSessionId, cancellationToken);
        var localNow = VietnamTime.Now;
        if (session is null
            || session.Status != "Published"
            || session.Booking.Status != "Confirmed"
            || session.Booking.StartTime <= localNow)
            return NotFound(new { message = "Không tìm thấy phiên vé." });

        return Ok(MapSession(session, DateTime.UtcNow, localNow));
    }

    public async Task<ServiceResult<PaginatedResponse<TicketSessionResponse>>> GetOwnerSessions(
        int? userId,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();

        var query = _paymentRepository.TicketSessions.AsNoTracking()
            .AsSingleQuery()
            .Include(session => session.Booking).ThenInclude(booking => booking.Court).ThenInclude(court => court.Venue)
            .Include(session => session.Tickets).ThenInclude(ticket => ticket.Payment)
            .Where(session => session.Booking.Court.Venue.Owner.UserId == userId.Value);

        var localNow = VietnamTime.Now;
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
                ? query.Where(session => session.Status == "Published" && session.Booking.EndTime <= localNow)
                : status.Equals("Published", StringComparison.OrdinalIgnoreCase)
                    ? query.Where(session => session.Status == "Published" && session.Booking.EndTime > localNow)
                    : query.Where(session => session.Status == status);
        }

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var items = await query
            .OrderByDescending(session => session.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(Pagination.Create(items.Select(item => MapSession(item, now, localNow)).ToList(), totalCount, page, pageSize));
    }

    public async Task<ServiceResult<TicketSessionResponse>> GetOwnerSession(
        int sessionId,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();

        var session = await LoadSessionReadAsync(sessionId, cancellationToken);
        if (session is null || session.Booking.Court.Venue.Owner.UserId != userId.Value)
            return NotFound(new { message = "Không tìm thấy phiên vé." });

        return Ok(MapSession(session, DateTime.UtcNow, VietnamTime.Now));
    }

    public async Task<ServiceResult<TicketSessionResponse>> CreateSession(
        int? userId,
        CreateTicketSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var dateError = ValidateAdvanceBookingDate(request.Date);
        if (dateError is not null) return BadRequest(new { message = dateError });
        var startTime = request.Date.ToDateTime(request.StartTime);
        var endTime = request.Date.ToDateTime(request.EndTime);
        var timeError = ValidateSessionTime(startTime, endTime);
        if (timeError is not null) return BadRequest(new { message = timeError });
        if (request.MinSkillLevel > request.MaxSkillLevel)
            return BadRequest(new { message = "Trình độ tối thiểu không được lớn hơn trình độ tối đa." });
        if (request.TicketPrice != decimal.Truncate(request.TicketPrice))
            return BadRequest(new { message = "Giá vé phải là số nguyên VND." });

        var venue = await _paymentRepository.Venues
            .Include(item => item.Owner)
            .Include(item => item.Courts)
            .SingleOrDefaultAsync(item => item.VenueId == request.VenueId
                && item.Owner.UserId == userId.Value, cancellationToken);
        var court = venue?.Courts.SingleOrDefault(item => item.CourtId == request.CourtId);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân thuộc quyền quản lý." });
        if (!venue!.IsOpen || venue.ApprovalStatus != "Approved" || court.AvailabilityStatus != "Available")
            return Conflict(new { message = "Sân hiện không sẵn sàng để tạo buổi xé vé." });
        if (request.StartTime < venue.OpenTime || request.EndTime > venue.CloseTime)
            return BadRequest(new { message = $"Khung giờ phải nằm trong giờ mở cửa {venue.OpenTime:HH:mm}–{venue.CloseTime:HH:mm}." });

        await using var transaction = await _paymentRepository.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction, CourtScheduleResource(court.CourtId, startTime), cancellationToken))
            return Conflict(new { message = "Sân đang được cập nhật. Vui lòng thử lại." });
        if (await HasCourtOverlap(court.CourtId, startTime, endTime, cancellationToken))
            return Conflict(new { message = "Khung giờ đã có booking hoặc lịch sân khác." });

        var utcNow = DateTime.UtcNow;
        var title = request.Title.Trim();
        var booking = new Booking
        {
            CourtId = court.CourtId,
            Court = court,
            StartTime = startTime,
            EndTime = endTime,
            Status = "Confirmed",
            OwnerEntryType = "TicketSession",
            Title = title,
            BookingCode = NewCode("TS"),
            CreatedAt = utcNow,
            HourlyPriceSnapshot = court.HourlyPrice,
            CourtAmount = 0,
            TotalAmount = 0
        };
        booking.StatusHistories.Add(new BookingStatusHistory
        {
            ToStatus = "Confirmed",
            Reason = "Owner giữ sân cho buổi xé vé",
            ActorUserId = userId,
            ChangedAt = utcNow
        });

        var session = new TicketSession
        {
            Booking = booking,
            Title = title,
            Description = NormalizeOptional(request.Description),
            SkillLevel = TicketingPolicy.FormatSkillRange(request.MinSkillLevel, request.MaxSkillLevel),
            PlayFormat = request.PlayFormat.Trim(),
            MaxPlayers = request.MaxPlayers,
            TicketPrice = request.TicketPrice,
            CancellationDeadlineHours = Math.Clamp(
                _configuration.GetValue("Ticketing:CancellationDeadlineHours", 24), 0, 168),
            Status = "Draft",
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        await _paymentRepository.AddTicketSessionAsync(session, cancellationToken);
        await _paymentRepository.AddAuditLogAsync(
            NewAudit(venue.VenueId, userId.Value, $"TicketSessionCreated:{title}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishSchedule(session, "Created");
        return Ok(MapSession(session, utcNow, VietnamTime.Now));
    }

    public async Task<ServiceResult<TicketSessionResponse>> UpdateSession(
        int? userId,
        int ticketSessionId,
        UpdateTicketSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction, $"ticket-session:{ticketSessionId}", cancellationToken))
            return Conflict(new { message = "Buổi xé vé đang được cập nhật. Vui lòng thử lại." });

        var session = await LoadSessionTrackedAsync(ticketSessionId, cancellationToken);
        if (session is null || session.Booking.Court.Venue.Owner.UserId != userId.Value)
            return NotFound(new { message = "Không tìm thấy phiên vé." });

        var utcNow = DateTime.UtcNow;
        var localNow = VietnamTime.Now;
        if (session.Status == "Cancelled")
            return Conflict(new { message = "Buổi xé vé đã bị hủy nên không thể chỉnh sửa." });
        if (session.Booking.StartTime <= localNow)
            return Conflict(new { message = "Buổi xé vé đã bắt đầu nên không thể chỉnh sửa." });

        var activeCount = SoldOrReservedTicketsCount(session, utcNow);
        if (request.MaxPlayers < activeCount)
            return BadRequest(new { message = $"Số lượng vé không thể nhỏ hơn số vé đã được bán/giữ ({activeCount})." });
        if (request.MinSkillLevel > request.MaxSkillLevel)
            return BadRequest(new { message = "Trình độ tối thiểu không được lớn hơn trình độ tối đa." });
        if (request.TicketPrice != decimal.Truncate(request.TicketPrice))
            return BadRequest(new { message = "Giá vé phải là số nguyên VND." });

        var currentDate = DateOnly.FromDateTime(session.Booking.StartTime);
        var currentStart = TimeOnly.FromDateTime(session.Booking.StartTime);
        var currentEnd = TimeOnly.FromDateTime(session.Booking.EndTime);
        var targetVenueId = request.VenueId ?? session.Booking.Court.VenueId;
        var targetCourtId = request.CourtId ?? session.Booking.CourtId;
        var targetDate = request.Date ?? currentDate;
        var targetStartValue = request.StartTime ?? currentStart;
        var targetEndValue = request.EndTime ?? currentEnd;
        var targetStart = targetDate.ToDateTime(targetStartValue);
        var targetEnd = targetDate.ToDateTime(targetEndValue);
        var scheduleChanged = targetVenueId != session.Booking.Court.VenueId
            || targetCourtId != session.Booking.CourtId
            || targetDate != currentDate
            || targetStartValue != currentStart
            || targetEndValue != currentEnd;
        var hasTickets = session.Tickets.Count != 0;
        if (hasTickets && scheduleChanged)
            return Conflict(new { message = "Không thể đổi sân, ngày hoặc giờ sau khi đã phát sinh lượt mua vé." });
        if (hasTickets && request.TicketPrice != session.TicketPrice)
            return Conflict(new { message = "Không thể đổi giá vé sau khi đã phát sinh lượt mua vé." });

        if (scheduleChanged)
        {
            var dateError = ValidateAdvanceBookingDate(targetDate);
            if (dateError is not null) return BadRequest(new { message = dateError });
            var timeError = ValidateSessionTime(targetStart, targetEnd);
            if (timeError is not null) return BadRequest(new { message = timeError });

            var scheduleLocks = new[]
            {
                CourtScheduleResource(session.Booking.CourtId, session.Booking.StartTime),
                CourtScheduleResource(targetCourtId, targetStart)
            }.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal);
            foreach (var resource in scheduleLocks)
            {
                if (!await SqlServerBookingLock.AcquireAsync(transaction, resource, cancellationToken))
                    return Conflict(new { message = "Lịch sân đang được cập nhật. Vui lòng thử lại." });
            }
        }

        var venue = await _paymentRepository.Venues
            .Include(item => item.Owner)
            .Include(item => item.Courts)
            .SingleOrDefaultAsync(item => item.VenueId == targetVenueId
                && item.Owner.UserId == userId.Value, cancellationToken);
        var court = venue?.Courts.SingleOrDefault(item => item.CourtId == targetCourtId);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân thuộc quyền quản lý." });
        if (scheduleChanged
            && (!venue!.IsOpen || venue.ApprovalStatus != "Approved" || court.AvailabilityStatus != "Available"))
            return Conflict(new { message = "Sân hiện không sẵn sàng để cập nhật buổi xé vé." });
        if (targetStartValue < venue!.OpenTime || targetEndValue > venue.CloseTime)
            return BadRequest(new { message = $"Khung giờ phải nằm trong giờ mở cửa {venue.OpenTime:HH:mm}–{venue.CloseTime:HH:mm}." });
        if (scheduleChanged && await HasCourtOverlap(
                targetCourtId, targetStart, targetEnd, session.BookingId, cancellationToken))
            return Conflict(new { message = "Khung giờ đã có booking hoặc lịch sân khác." });

        var oldVenueId = session.Booking.Court.VenueId;
        var oldCourtId = session.Booking.CourtId;
        var oldStart = session.Booking.StartTime;
        var oldEnd = session.Booking.EndTime;

        session.Title = request.Title.Trim();
        session.Description = NormalizeOptional(request.Description);
        session.SkillLevel = TicketingPolicy.FormatSkillRange(request.MinSkillLevel, request.MaxSkillLevel);
        session.PlayFormat = NormalizeOptional(request.PlayFormat) ?? "Giao lưu tự do";
        session.TotalTickets = request.MaxPlayers;
        session.TicketPrice = request.TicketPrice;
        session.UpdatedAt = utcNow;
        session.Booking.CourtId = targetCourtId;
        session.Booking.Court = court;
        session.Booking.StartTime = targetStart;
        session.Booking.EndTime = targetEnd;
        session.Booking.Title = session.Title;
        session.Booking.HourlyPriceSnapshot = court.HourlyPrice;

        await _paymentRepository.AddAuditLogAsync(NewAudit(session.Booking.Court.VenueId, userId.Value, $"UpdatedTicketSession:{ticketSessionId}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (scheduleChanged)
            PublishSchedule(oldVenueId, oldCourtId, oldStart, oldEnd, "Updated");
        PublishSchedule(session, "Updated");
        return Ok(MapSession(session, utcNow, localNow));
    }

    public async Task<ServiceResult<TicketSessionResponse>> PublishSession(int? userId, int ticketSessionId, CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction, $"ticket-session:{ticketSessionId}", cancellationToken))
            return Conflict(new { message = "Buổi xé vé đang được cập nhật. Vui lòng thử lại." });
        var session = await LoadSessionTrackedAsync(ticketSessionId, cancellationToken);
        if (session is null || session.Booking.Court.Venue.Owner.UserId != userId.Value)
            return NotFound(new { message = "Không tìm thấy phiên vé." });

        if (session.Status != "Draft")
            return Conflict(new { message = "Chỉ bản nháp mới có thể đăng bán." });
        if (session.Booking.Status != "Confirmed" || session.Booking.StartTime <= VietnamTime.Now)
            return Conflict(new { message = "Booking của buổi xé vé đã bắt đầu hoặc không còn hoạt động." });

        var utcNow = DateTime.UtcNow;
        session.Status = "Published";
        session.PublishedAt = utcNow;
        session.UpdatedAt = utcNow;
        await _paymentRepository.AddAuditLogAsync(NewAudit(
            session.Booking.Court.VenueId, userId.Value, $"PublishedTicketSession:{ticketSessionId}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishSchedule(session, "Updated");
        return Ok(MapSession(session, utcNow, VietnamTime.Now));
    }

    public async Task<ServiceResult<TicketSessionResponse>> CancelSession(int? userId, int ticketSessionId, CancelTicketSessionRequest request, CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction, $"ticket-session:{ticketSessionId}", cancellationToken))
            return Conflict(new { message = "Buổi xé vé đang được cập nhật. Vui lòng thử lại." });
        var session = await LoadSessionTrackedAsync(ticketSessionId, cancellationToken);
        if (session is null || session.Booking.Court.Venue.Owner.UserId != userId.Value)
            return NotFound(new { message = "Không tìm thấy phiên vé." });

        if (session.Status == "Cancelled")
            return Ok(MapSession(session, DateTime.UtcNow, VietnamTime.Now));
        if (session.Booking.StartTime <= VietnamTime.Now)
            return Conflict(new { message = "Buổi xé vé đã bắt đầu nên không thể hủy." });
        if (session.Tickets.Any(ticket => ticket.Status == "CheckedIn" || ticket.CheckedInAt.HasValue))
            return Conflict(new { message = "Buổi xé vé đã có người check-in nên không thể hủy." });
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction, CourtScheduleResource(session.Booking.CourtId, session.Booking.StartTime), cancellationToken))
            return Conflict(new { message = "Lịch sân đang được cập nhật. Vui lòng thử lại." });

        var utcNow = DateTime.UtcNow;
        var reason = request.Reason.Trim();
        session.Status = "Cancelled";
        session.CancelledAt = utcNow;
        session.CancellationReason = reason;
        session.UpdatedAt = utcNow;
        var bookingFrom = session.Booking.Status;
        session.Booking.Status = "Cancelled";
        session.Booking.StatusHistories.Add(new BookingStatusHistory
        {
            FromStatus = bookingFrom,
            ToStatus = "Cancelled",
            Reason = reason,
            ActorUserId = userId.Value,
            ChangedAt = utcNow
        });

        var changedPayments = new List<Payment>();
        foreach (var ticket in session.Tickets)
        {
            if (ticket.Status is "Cancelled" or "RefundPending" or "Refunded") continue;
            var shouldNotify = ticket.Status is "PendingPayment" or "Paid";
            ticket.Status = "Cancelled";
            ticket.HoldExpiresAt = null;
            ticket.CancelledAt = utcNow;
            ticket.CancellationReason = reason;

            if (ticket.Payment.Status is "Pending" or "WaitingForConfirmation")
            {
                var paymentFrom = ticket.Payment.Status;
                ticket.Payment.Status = "Cancelled";
                ticket.Payment.StatusHistories.Add(NewPaymentHistory(
                    ticket.Payment.PaymentId, paymentFrom, "Cancelled", reason));
                changedPayments.Add(ticket.Payment);
            }

            if (shouldNotify)
            {
                _notifications.Add(new NotificationInput(
                    ticket.Player.UserId,
                    NotificationTypes.Ticket,
                    "Buổi xé vé đã bị hủy",
                    $"Buổi {session.Title} đã bị hủy. Vé đã thanh toán không được hoàn tiền theo chính sách.",
                    NotificationTones.Urgent,
                    $"/my-tickets/{ticket.SessionTicketId}",
                    "Xem vé"));
            }
        }

        await _paymentRepository.AddAuditLogAsync(NewAudit(
            session.Booking.Court.VenueId, userId.Value, $"CancelledTicketSession:{ticketSessionId}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishPayments(changedPayments, "Cancelled");
        PublishSchedule(session, "Cancelled");
        return Ok(MapSession(session, utcNow, VietnamTime.Now));
    }

    public async Task<ServiceResult<TicketSessionParticipantsResponse>> GetOwnerParticipants(int? userId, int ticketSessionId, CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var session = await LoadSessionReadAsync(ticketSessionId, cancellationToken);
        if (session is null || session.Booking.Court.Venue.Owner.UserId != userId.Value)
            return NotFound(new { message = "Không tìm thấy phiên vé." });

        var response = new TicketSessionParticipantsResponse
        {
            Session = MapSession(session, DateTime.UtcNow, VietnamTime.Now),
            Tickets = session.Tickets.Select(t => MapTicket(t, DateTime.UtcNow)).ToList()
        };
        return Ok(response);
    }

    private Task<TicketSession?> LoadSessionReadAsync(int sessionId, CancellationToken cancellationToken) =>
        _paymentRepository.TicketSessions.AsNoTracking()
            .AsSingleQuery()
            .Include(s => s.Booking).ThenInclude(b => b.Court).ThenInclude(c => c.Venue).ThenInclude(v => v.Owner)
            .Include(s => s.Tickets).ThenInclude(t => t.Player).ThenInclude(p => p.User)
            .Include(s => s.Tickets).ThenInclude(t => t.Payment).ThenInclude(p => p.SePayTransactions)
            .SingleOrDefaultAsync(s => s.TicketSessionId == sessionId, cancellationToken);

    private Task<TicketSession?> LoadSessionTrackedAsync(int sessionId, CancellationToken cancellationToken) =>
        _paymentRepository.TicketSessions
            .AsSingleQuery()
            .Include(s => s.Booking).ThenInclude(b => b.Court).ThenInclude(c => c.Venue).ThenInclude(v => v.Owner)
            .Include(s => s.Booking).ThenInclude(b => b.StatusHistories)
            .Include(s => s.Tickets).ThenInclude(t => t.Player).ThenInclude(p => p.User)
            .Include(s => s.Tickets).ThenInclude(t => t.Payment).ThenInclude(p => p.StatusHistories)
            .SingleOrDefaultAsync(s => s.TicketSessionId == sessionId, cancellationToken);

    private static int AvailableTicketsCount(TicketSession session, DateTime now)
    {
        var active = SoldOrReservedTicketsCount(session, now);
        return Math.Max(0, session.TotalTickets - active);
    }

    private static int SoldOrReservedTicketsCount(TicketSession session, DateTime now)
    {
        return session.Tickets.Count(ticket =>
            ticket.Status is "Paid" or "CheckedIn" ||
            ticket.Payment.Status == "WaitingForConfirmation" ||
            (ticket.Status == "PendingPayment" && ticket.HoldExpiresAt > now));
    }

    private static int ReservedTicketsCount(TicketSession session, DateTime now) =>
        session.Tickets.Count(ticket =>
            ticket.Payment.Status == "WaitingForConfirmation"
            || ticket.Status == "PendingPayment" && ticket.HoldExpiresAt > now);

    private Task<bool> HasCourtOverlap(
        int courtId,
        DateTime startTime,
        DateTime endTime,
        int? excludedBookingId,
        CancellationToken cancellationToken) =>
        _paymentRepository.Bookings.AnyAsync(booking =>
            (!excludedBookingId.HasValue || booking.BookingId != excludedBookingId.Value)
            && booking.Status != "Cancelled"
            && booking.Status != "Expired"
            && (booking.Status != "Holding" || booking.HoldExpiresAt > DateTime.UtcNow)
            && (booking.Slots.Any(slot => slot.CourtId == courtId
                    && slot.StartTime < endTime && slot.EndTime > startTime)
                || !booking.Slots.Any() && booking.CourtId == courtId
                    && booking.StartTime < endTime && booking.EndTime > startTime),
            cancellationToken);

    private Task<bool> HasCourtOverlap(
        int courtId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken) =>
        HasCourtOverlap(courtId, startTime, endTime, null, cancellationToken);

    private static string? ValidateSessionTime(DateTime startTime, DateTime endTime)
    {
        if (endTime <= startTime) return "Giờ kết thúc phải sau giờ bắt đầu trong cùng ngày.";
        if (startTime <= VietnamTime.Now) return "Không thể tạo buổi xé vé trong quá khứ.";
        if (startTime.Minute % 30 != 0 || endTime.Minute % 30 != 0
            || startTime.Second != 0 || endTime.Second != 0
            || (endTime - startTime).TotalMinutes % 30 != 0)
            return "Thời gian phải theo bước 30 phút.";
        return null;
    }

    private static string? ValidateAdvanceBookingDate(DateOnly date)
    {
        var today = DateOnly.FromDateTime(VietnamTime.Now);
        var maxBookingDate = new DateOnly(today.Year, today.Month, 1)
            .AddMonths(MaximumAdvanceBookingMonths + 1)
            .AddDays(-1);
        return date < today || date > maxBookingDate
            ? "Chỉ được tạo buổi xé vé từ hôm nay đến hết tháng kế tiếp."
            : null;
    }

    private static string CourtScheduleResource(int courtId, DateTime startTime) =>
        $"court-schedule:{courtId}:{startTime:yyyyMMdd}";

    private static TicketSessionResponse MapSession(TicketSession session, DateTime now, DateTime? localNow = null)
    {
        var available = AvailableTicketsCount(session, now);
        var reserved = ReservedTicketsCount(session, now);
        var effectiveLocalNow = localNow ?? VietnamTime.Now;
        var (minSkillLevel, maxSkillLevel) = TicketingPolicy.ParseSkillRange(session.SkillLevel);
        return new TicketSessionResponse
        {
            TicketSessionId = session.TicketSessionId,
            BookingId = session.BookingId,
            VenueId = session.Booking.Court.VenueId,
            VenueName = session.Booking.Court.Venue.VenueName,
            VenueAddress = session.Booking.Court.Venue.Address,
            VenuePhone = session.Booking.Court.Venue.PhoneNumber,
            VenueLatitude = session.Booking.Court.Venue.Latitude,
            VenueLongitude = session.Booking.Court.Venue.Longitude,
            CourtId = session.Booking.CourtId,
            CourtNumber = session.Booking.Court.CourtNumber,
            CourtType = session.Booking.Court.CourtType,
            Title = session.Title,
            Description = session.Description,
            SkillLevel = session.SkillLevel,
            MinSkillLevel = minSkillLevel,
            MaxSkillLevel = maxSkillLevel,
            PlayFormat = session.PlayFormat,
            TotalTickets = session.TotalTickets,
            AvailableTickets = available,
            SoldTickets = session.Tickets.Count(t => t.Status is "Paid" or "CheckedIn"),
            ReservedTickets = reserved,
            TicketPrice = session.TicketPrice,
            CancellationDeadlineHours = session.CancellationDeadlineHours,
            Status = session.Status == "Published" && session.Booking.EndTime <= effectiveLocalNow
                ? "Completed"
                : session.Status,
            StartTime = session.Booking.StartTime,
            EndTime = session.Booking.EndTime,
            CreatedAt = session.CreatedAt,
            PublishedAt = session.PublishedAt,
            CancelledAt = session.CancelledAt,
            CancellationReason = session.CancellationReason
        };
    }

    private static SessionTicketResponse MapTicket(SessionTicket ticket, DateTime now, bool includeSession = true) => new()
    {
        SessionTicketId = ticket.SessionTicketId,
        TicketSessionId = ticket.TicketSessionId,
        TicketCode = ticket.TicketCode,
        PlayerId = ticket.PlayerId,
        PlayerName = ticket.Player.User.Username,
        PlayerEmail = ticket.Player.User.Email,
        PlayerProfileImageUrl = ticket.Player.User.ProfileImageUrl,
        Status = ticket.Status == "Cancelled" && ticket.Payment.Status == "Cancelled" && ticket.Payment.PaidAt is null
            ? "Expired"
            : ticket.Status,
        CancellationReason = ticket.CancellationReason,
        Amount = ticket.Payment.Amount,
        PaymentId = ticket.PaymentId,
        PaymentStatus = ticket.Payment.Status == "Cancelled" && ticket.Payment.PaidAt is null
            ? "Expired"
            : ticket.Payment.Status,
        TransferContent = ticket.Payment.TransferContent,
        BankCode = ticket.Payment.BankCode,
        BankName = ticket.Payment.BankName,
        BankAccountNumber = ticket.Payment.BankAccountNumber,
        BankAccountName = ticket.Payment.BankAccountName,
        QrImageUrl = ticket.Payment.QrImageUrl,
        ReceiptImageUrl = ticket.Payment.ReceiptImageUrl,
        RejectionReason = ticket.Payment.RejectionReason,
        PaidAt = ticket.Payment.PaidAt,
        SePayTransactions = ticket.Payment.SePayTransactions.Select(item => new SePayTransactionResponse
        {
            SePayTransactionId = item.SePayTransactionId,
            ExternalTransactionId = item.ExternalTransactionId,
            Amount = item.Amount,
            Status = item.Status,
            ReceivedAt = item.ReceivedAt,
            RefundedAt = item.RefundedAt,
            RefundReference = item.RefundReference
        }).ToList(),
        HoldExpiresAt = ticket.HoldExpiresAt,
        HoldRemainingSeconds = ticket.HoldExpiresAt.HasValue && ticket.HoldExpiresAt.Value > now
            ? (int)Math.Ceiling((ticket.HoldExpiresAt.Value - now).TotalSeconds)
            : null,
        CancelledAt = ticket.CancelledAt,
        CheckedInAt = ticket.CheckedInAt,
        CheckedInByStaffId = ticket.CheckedInByStaffId,
        CreatedAt = ticket.CreatedAt,
        HasSePayApiToken = ticket.TicketSession?.Booking?.Court?.Venue?.Owner?.BankAccounts?.Any(a => a.IsActive && !string.IsNullOrEmpty(a.SePayApiToken)) ?? false,
        Session = includeSession && ticket.TicketSession != null ? MapSession(ticket.TicketSession, now, VietnamTime.Now) : null
    };

    private static ServiceResult Ok(object? value = null) => new(ServiceResultStatus.Success, value);
    private static ServiceResult BadRequest(object? error = null) => new(ServiceResultStatus.BadRequest, Error: error);
    private static ServiceResult Unauthorized(object? error = null) => new(ServiceResultStatus.Unauthorized, Error: error);
    private static ServiceResult Forbidden(object? error = null) => new(ServiceResultStatus.Forbidden, Error: error);
    private static ServiceResult NotFound(object? error = null) => new(ServiceResultStatus.NotFound, Error: error);
    private static ServiceResult Conflict(object? error = null) => new(ServiceResultStatus.Conflict, Error: error);

    private static VenueAuditLog NewAudit(int venueId, int actorId, string action) => new()
    {
        VenueId = venueId,
        ActorId = actorId,
        Action = action,
        Timestamp = DateTime.UtcNow
    };

    private void PublishSchedule(TicketSession session, string action)
    {
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            session.Booking.Court.VenueId,
            session.Booking.CourtId,
            session.Booking.StartTime,
            session.Booking.EndTime,
            session.Booking.Status,
            action));
    }

    private void PublishSchedule(
        int venueId,
        int courtId,
        DateTime startTime,
        DateTime endTime,
        string action)
    {
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            venueId,
            courtId,
            startTime,
            endTime,
            "Confirmed",
            action));
    }

    private static string Normalize(string input) => input.Trim();

    private static IQueryable<TicketSession> SessionGraph(IQueryable<TicketSession> query) => query
        .AsSingleQuery()
        .Include(item => item.Booking).ThenInclude(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner).ThenInclude(item => item.User)
        .Include(item => item.Tickets).ThenInclude(item => item.Payment)
        .Include(item => item.Tickets).ThenInclude(item => item.Player).ThenInclude(item => item.User);

    private static PaymentStatusHistory NewPaymentHistory(int paymentId, string oldStatus, string newStatus, string? note = null) => new()
    {
        PaymentId = paymentId,
        OldStatus = oldStatus,
        NewStatus = newStatus,
        Note = note,
        ChangedAt = DateTime.UtcNow
    };

    private static string NewCode(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    private void PublishPayments(IEnumerable<Payment> payments, string action)
    {
        foreach (var payment in payments)
        {
            _paymentRealtime.Publish(new PaymentChangedEvent(
                payment.PaymentId,
                payment.BookingId,
                payment.Status,
                action));
        }
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
