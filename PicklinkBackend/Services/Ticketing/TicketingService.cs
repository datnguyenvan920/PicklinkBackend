using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Payments;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Ticketing;

public sealed partial class TicketingService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly PlayerScheduleConflictService _playerScheduleConflict;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly PaymentRealtimeNotifier _paymentRealtime;
    private readonly NotificationService _notifications;

    public TicketingService(
        ApplicationDbContext db,
        IConfiguration configuration,
        PlayerScheduleConflictService playerScheduleConflict,
        ScheduleRealtimeNotifier scheduleRealtime,
        PaymentRealtimeNotifier paymentRealtime,
        NotificationService notifications)
    {
        _db = db;
        _configuration = configuration;
        _playerScheduleConflict = playerScheduleConflict;
        _scheduleRealtime = scheduleRealtime;
        _paymentRealtime = paymentRealtime;
        _notifications = notifications;
    }

    public async Task<ServiceResult<PaginatedResponse<TicketSessionResponse>>> GetPublishedSessions(
        string? search,
        int? venueId,
        DateOnly? date,
        string? skillLevel,
        string? playFormat,
        decimal? minPrice,
        decimal? maxPrice,
        bool onlyAvailable,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (minPrice is < 0 || maxPrice is < 0 || minPrice > maxPrice)
            return BadRequest(new { message = "Khoảng giá vé không hợp lệ." });

        var localNow = VietnamTime.Now;
        var utcNow = DateTime.UtcNow;
        var query = _db.TicketSessions
            .Where(item => item.Status == "Published" && item.Booking.EndTime > localNow);
        var keyword = Normalize(search);
        if (keyword is not null)
            query = query.Where(item => item.Title.Contains(keyword)
                || item.Booking.Court.Venue.VenueName.Contains(keyword)
                || item.Booking.Court.Venue.Address.Contains(keyword));
        if (venueId.HasValue) query = query.Where(item => item.Booking.Court.VenueId == venueId.Value);
        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);
            query = query.Where(item => item.Booking.StartTime >= start && item.Booking.StartTime < end);
        }
        if (!string.IsNullOrWhiteSpace(skillLevel))
            query = query.Where(item => item.SkillLevel == skillLevel.Trim());
        if (!string.IsNullOrWhiteSpace(playFormat))
            query = query.Where(item => item.PlayFormat == playFormat.Trim());
        if (minPrice.HasValue) query = query.Where(item => item.TicketPrice >= minPrice.Value);
        if (maxPrice.HasValue) query = query.Where(item => item.TicketPrice <= maxPrice.Value);
        if (onlyAvailable)
            query = query.Where(item => item.Tickets.Count(ticket =>
                ticket.Status == "Paid"
                || ticket.Status == "CheckedIn"
                || ticket.Status == "PendingPayment" && ticket.HoldExpiresAt > utcNow) < item.MaxPlayers);

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var sessions = await SessionGraph(query.AsNoTracking())
            .OrderBy(item => item.Booking.StartTime)
            .ThenBy(item => item.TicketSessionId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(Pagination.Create(
            sessions.Select(item => MapSession(item, utcNow, localNow)),
            totalCount,
            page,
            pageSize));
    }

    public async Task<ServiceResult<TicketSessionResponse>> GetPublishedSession(
        int ticketSessionId,
        CancellationToken cancellationToken)
    {
        var session = await SessionGraph(_db.TicketSessions.AsNoTracking())
            .SingleOrDefaultAsync(item => item.TicketSessionId == ticketSessionId && item.Status == "Published", cancellationToken);
        return session is null
            ? NotFound(new { message = "Không tìm thấy buổi xé vé." })
            : Ok(MapSession(session, DateTime.UtcNow, VietnamTime.Now));
    }

    public async Task<ServiceResult<PaginatedResponse<TicketSessionResponse>>> GetOwnerSessions(
        int? userId,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var localNow = VietnamTime.Now;
        var utcNow = DateTime.UtcNow;
        var query = _db.TicketSessions.Where(item => item.Booking.Court.Venue.Owner.UserId == userId.Value);
        var normalizedStatus = Normalize(status);
        if (normalizedStatus?.Equals("Completed", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(item => item.Status == "Published" && item.Booking.EndTime <= localNow);
        else if (normalizedStatus is not null)
            query = query.Where(item => item.Status == normalizedStatus);

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var sessions = await SessionGraph(query.AsNoTracking())
            .OrderByDescending(item => item.Booking.StartTime)
            .ThenByDescending(item => item.TicketSessionId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(Pagination.Create(
            sessions.Select(item => MapSession(item, utcNow, localNow)),
            totalCount,
            page,
            pageSize));
    }

    public async Task<ServiceResult<TicketSessionResponse>> CreateSession(
        int? userId,
        CreateTicketSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var startTime = request.Date.ToDateTime(request.StartTime);
        var endTime = request.Date.ToDateTime(request.EndTime);
        var timeError = ValidateSessionTime(startTime, endTime);
        if (timeError is not null) return BadRequest(new { message = timeError });
        if (request.TicketPrice != decimal.Truncate(request.TicketPrice))
            return BadRequest(new { message = "Giá vé phải là số nguyên VND." });

        var court = await _db.Courts
            .Include(item => item.Venue).ThenInclude(item => item.Owner)
            .SingleOrDefaultAsync(item => item.CourtId == request.CourtId
                && item.VenueId == request.VenueId
                && item.Venue.Owner.UserId == userId.Value, cancellationToken);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân thuộc quyền quản lý." });
        if (!court.Venue.IsOpen || court.Venue.ApprovalStatus != "Approved" || court.AvailabilityStatus != "Available")
            return Conflict(new { message = "Sân hiện không sẵn sàng để tạo buổi xé vé." });
        if (request.StartTime < court.Venue.OpenTime || request.EndTime > court.Venue.CloseTime)
            return BadRequest(new { message = $"Khung giờ phải nằm trong giờ mở cửa {court.Venue.OpenTime:HH:mm}–{court.Venue.CloseTime:HH:mm}." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"court-booking:{court.CourtId}", cancellationToken))
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
            BookingCode = NewCode("TS", 30),
            CreatedAt = utcNow,
            HourlyPriceSnapshot = court.HourlyPrice,
            CourtAmount = 0,
            TotalAmount = 0
        };
        booking.StatusHistories.Add(new BookingStatusHistory
        {
            FromStatus = null,
            ToStatus = "Confirmed",
            Reason = "Owner giữ sân cho buổi xé vé",
            ActorUserId = userId,
            ChangedAt = utcNow
        });
        var session = new TicketSession
        {
            Booking = booking,
            Title = title,
            Description = Normalize(request.Description),
            SkillLevel = request.SkillLevel.Trim(),
            PlayFormat = request.PlayFormat.Trim(),
            MaxPlayers = request.MaxPlayers,
            TicketPrice = request.TicketPrice,
            CancellationDeadlineHours = Math.Clamp(_configuration.GetValue("Ticketing:CancellationDeadlineHours", 24), 0, 168),
            Status = "Draft",
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
        _db.TicketSessions.Add(session);
        AddAudit(court.VenueId, userId.Value, $"TicketSessionCreated:{title}", utcNow);
        await _db.SaveChangesAsync(cancellationToken);
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
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"ticket-session:{ticketSessionId}", cancellationToken))
            return Conflict(new { message = "Buổi xé vé đang được cập nhật." });
        var session = await OwnedSessionGraph(userId.Value)
            .SingleOrDefaultAsync(item => item.TicketSessionId == ticketSessionId, cancellationToken);
        if (session is null) return NotFound(new { message = "Không tìm thấy buổi xé vé." });
        if (session.Status is not ("Draft" or "Published") || session.Booking.StartTime <= VietnamTime.Now)
            return Conflict(new { message = "Buổi xé vé không còn cho phép chỉnh sửa." });

        var targetCourtId = request.CourtId ?? session.Booking.CourtId;
        var targetDate = request.Date ?? DateOnly.FromDateTime(session.Booking.StartTime);
        var targetStartOfDay = request.StartTime ?? TimeOnly.FromDateTime(session.Booking.StartTime);
        var targetEndOfDay = request.EndTime ?? TimeOnly.FromDateTime(session.Booking.EndTime);
        var targetStart = targetDate.ToDateTime(targetStartOfDay);
        var targetEnd = targetDate.ToDateTime(targetEndOfDay);
        var scheduleChanged = targetCourtId != session.Booking.CourtId
            || request.VenueId.HasValue && request.VenueId.Value != session.Booking.Court.VenueId
            || targetStart != session.Booking.StartTime
            || targetEnd != session.Booking.EndTime;
        ScheduleChangedEvent? previousSchedule = null;
        if (scheduleChanged)
        {
            if (session.Tickets.Count > 0)
                return Conflict(new
                {
                    message = "Không thể đổi sân hoặc giờ sau khi đã phát sinh lượt mua vé."
                });
            var timeError = ValidateSessionTime(targetStart, targetEnd);
            if (timeError is not null) return BadRequest(new { message = timeError });
            var targetCourt = await _db.Courts
                .Include(item => item.Venue).ThenInclude(item => item.Owner)
                .SingleOrDefaultAsync(item =>
                    item.CourtId == targetCourtId
                    && (!request.VenueId.HasValue || item.VenueId == request.VenueId.Value)
                    && item.Venue.Owner.UserId == userId.Value,
                    cancellationToken);
            if (targetCourt is null)
                return NotFound(new { message = "Không tìm thấy sân đích thuộc quyền quản lý." });
            if (!targetCourt.Venue.IsOpen
                || targetCourt.Venue.ApprovalStatus != "Approved"
                || targetCourt.AvailabilityStatus != "Available")
                return Conflict(new { message = "Sân đích hiện không sẵn sàng." });
            if (targetStartOfDay < targetCourt.Venue.OpenTime
                || targetEndOfDay > targetCourt.Venue.CloseTime)
                return BadRequest(new
                {
                    message = $"Khung giờ phải nằm trong giờ mở cửa {targetCourt.Venue.OpenTime:HH:mm}–{targetCourt.Venue.CloseTime:HH:mm}."
                });
            if (!await SqlServerBookingLock.AcquireAsync(
                    _db, transaction, $"court-booking:{targetCourtId}", cancellationToken))
                return Conflict(new { message = "Sân đích đang được cập nhật. Vui lòng thử lại." });
            if (await HasCourtOverlap(
                    targetCourtId,
                    targetStart,
                    targetEnd,
                    cancellationToken,
                    session.BookingId))
                return Conflict(new { message = "Khung giờ mới đã có booking hoặc lịch sân khác." });

            previousSchedule = new ScheduleChangedEvent(
                session.Booking.Court.VenueId,
                session.Booking.CourtId,
                session.Booking.StartTime,
                session.Booking.EndTime,
                "TicketSession",
                "Deleted");
            session.Booking.CourtId = targetCourt.CourtId;
            session.Booking.Court = targetCourt;
            session.Booking.StartTime = targetStart;
            session.Booking.EndTime = targetEnd;
        }

        var utcNow = DateTime.UtcNow;
        var activeTickets = session.Tickets.Count(item => TicketingPolicy.OccupiesCapacity(item.Status, item.HoldExpiresAt, utcNow));
        if (request.MaxPlayers < activeTickets)
            return Conflict(new { message = $"Số người tối đa không được nhỏ hơn {activeTickets} vé đang giữ chỗ hoặc đã bán." });
        if (request.TicketPrice != decimal.Truncate(request.TicketPrice))
            return BadRequest(new { message = "Giá vé phải là số nguyên VND." });
        var requestedPrice = request.TicketPrice;
        var priceLocked = session.Tickets.Any(item =>
            item.Status is "PendingPayment" or "Paid" or "CheckedIn" or "Expired");
        if (priceLocked && requestedPrice != session.TicketPrice)
            return Conflict(new { message = "Không thể đổi giá sau khi đã phát sinh lượt giữ hoặc mua vé." });

        session.Title = request.Title.Trim();
        session.Booking.Title = session.Title;
        session.Description = Normalize(request.Description);
        session.SkillLevel = request.SkillLevel.Trim();
        session.PlayFormat = request.PlayFormat.Trim();
        session.MaxPlayers = request.MaxPlayers;
        session.TicketPrice = requestedPrice;
        session.UpdatedAt = utcNow;
        AddAudit(session.Booking.Court.VenueId, userId.Value, $"TicketSessionUpdated:{ticketSessionId}", utcNow);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (previousSchedule is not null)
        {
            _scheduleRealtime.Publish(previousSchedule);
            PublishSchedule(session, "Created");
        }
        else PublishSchedule(session, "Updated");
        return Ok(MapSession(session, utcNow, VietnamTime.Now));
    }

    public async Task<ServiceResult<TicketSessionResponse>> PublishSession(
        int? userId,
        int ticketSessionId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"ticket-session:{ticketSessionId}", cancellationToken))
            return Conflict(new { message = "Buổi xé vé đang được cập nhật." });
        var session = await OwnedSessionGraph(userId.Value)
            .SingleOrDefaultAsync(item => item.TicketSessionId == ticketSessionId, cancellationToken);
        if (session is null) return NotFound(new { message = "Không tìm thấy buổi xé vé." });
        if (session.Status == "Published") return Ok(MapSession(session, DateTime.UtcNow, VietnamTime.Now));
        if (session.Status != "Draft" || session.Booking.StartTime <= VietnamTime.Now)
            return Conflict(new { message = "Chỉ có thể đăng buổi nháp chưa bắt đầu." });
        if (session.TicketPrice > 0 && !await _db.OwnerBankAccounts.AnyAsync(
                item => item.OwnerId == session.Booking.Court.Venue.OwnerId && item.IsActive,
                cancellationToken))
            return Conflict(new { message = "Owner cần cấu hình tài khoản nhận tiền trước khi đăng bán vé." });

        var utcNow = DateTime.UtcNow;
        session.Status = "Published";
        session.PublishedAt = utcNow;
        session.UpdatedAt = utcNow;
        AddAudit(session.Booking.Court.VenueId, userId.Value, $"TicketSessionPublished:{ticketSessionId}", utcNow);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishSchedule(session, "Updated");
        return Ok(MapSession(session, utcNow, VietnamTime.Now));
    }

    public async Task<ServiceResult<TicketSessionResponse>> CancelSession(
        int? userId,
        int ticketSessionId,
        CancelTicketSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"ticket-session:{ticketSessionId}", cancellationToken))
            return Conflict(new { message = "Buổi xé vé đang được cập nhật." });
        var session = await OwnedSessionGraph(userId.Value)
            .SingleOrDefaultAsync(item => item.TicketSessionId == ticketSessionId, cancellationToken);
        if (session is null) return NotFound(new { message = "Không tìm thấy buổi xé vé." });
        if (session.Status == "Cancelled") return Ok(MapSession(session, DateTime.UtcNow, VietnamTime.Now));
        if (session.Booking.EndTime <= VietnamTime.Now)
            return Conflict(new { message = "Không thể hủy buổi đã kết thúc." });

        var utcNow = DateTime.UtcNow;
        var reason = request.Reason.Trim();
        var changedPayments = new List<Payment>();
        session.Status = "Cancelled";
        session.CancelledAt = utcNow;
        session.CancellationReason = reason;
        session.UpdatedAt = utcNow;
        var previousBookingStatus = session.Booking.Status;
        session.Booking.Status = "Cancelled";
        session.Booking.StatusHistories.Add(new BookingStatusHistory
        {
            FromStatus = previousBookingStatus,
            ToStatus = "Cancelled",
            Reason = $"Owner hủy buổi xé vé: {reason}",
            ActorUserId = userId,
            ChangedAt = utcNow
        });
        foreach (var ticket in session.Tickets.Where(item => item.Status is not ("Cancelled" or "Refunded")))
        {
            ticket.CancelledAt = utcNow;
            ticket.CancellationReason = reason;
            ticket.HoldExpiresAt = null;
            var paymentFrom = ticket.Payment.Status;
            var needsRefund = paymentFrom == "RefundPending"
                || ticket.Status == "RefundPending"
                || ticket.Payment.Amount > 0
                && (paymentFrom is "Paid" or "WaitingForConfirmation"
                    || ticket.Status is "Paid" or "CheckedIn");
            ticket.Status = needsRefund ? "RefundPending" : "Cancelled";
            ticket.Payment.Status = needsRefund ? "RefundPending" : "Cancelled";
            ticket.Payment.StatusHistories.Add(NewPaymentHistory(
                paymentFrom,
                ticket.Payment.Status,
                "TicketSessionCancelled",
                reason,
                userId));
            changedPayments.Add(ticket.Payment);
            _notifications.Add(new NotificationInput(
                ticket.Player.UserId,
                NotificationTypes.Ticket,
                "Buổi xé vé đã bị hủy",
                needsRefund
                    ? $"Buổi {session.Title} đã bị hủy. Vé của bạn đang chờ hoàn tiền."
                    : $"Buổi {session.Title} đã bị hủy.",
                NotificationTones.Urgent,
                "/my-tickets",
                "Xem vé"));
        }
        AddAudit(session.Booking.Court.VenueId, userId.Value, $"TicketSessionCancelled:{ticketSessionId}:{reason}", utcNow);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishSchedule(session, "Deleted");
        PublishPayments(changedPayments, "SessionCancelled");
        return Ok(MapSession(session, utcNow, VietnamTime.Now));
    }

    public async Task<ServiceResult<TicketSessionParticipantsResponse>> GetOwnerParticipants(
        int? userId,
        int ticketSessionId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var session = await OwnedSessionGraph(userId.Value, asTracking: false)
            .SingleOrDefaultAsync(item => item.TicketSessionId == ticketSessionId, cancellationToken);
        if (session is null) return NotFound(new { message = "Không tìm thấy buổi xé vé." });
        return Ok(MapParticipants(session));
    }

    public async Task<ServiceResult<SessionTicketResponse>> CompleteRefund(
        int? userId,
        int ticketSessionId,
        int sessionTicketId,
        CompleteTicketRefundRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"ticket-session:{ticketSessionId}", cancellationToken))
            return Conflict(new { message = "Buổi xé vé đang được cập nhật." });
        var session = await OwnedSessionGraph(userId.Value)
            .SingleOrDefaultAsync(item => item.TicketSessionId == ticketSessionId, cancellationToken);
        var ticket = session?.Tickets.SingleOrDefault(item => item.SessionTicketId == sessionTicketId);
        if (ticket is null) return NotFound(new { message = "Không tìm thấy vé cần hoàn tiền." });
        if (ticket.Status == "Refunded" && ticket.Payment.Status == "Refunded")
            return Ok(MapTicket(ticket, includeSession: false));
        if (ticket.Status != "RefundPending" || ticket.Payment.Status != "RefundPending")
            return Conflict(new { message = "Vé không ở trạng thái chờ hoàn tiền." });

        var utcNow = DateTime.UtcNow;
        ticket.Status = "Refunded";
        ticket.Payment.Status = "Refunded";
        ticket.Payment.VerifiedAt = utcNow;
        ticket.Payment.VerifiedByUserId = userId;
        foreach (var sePayTransaction in ticket.Payment.SePayTransactions.Where(item =>
                     item.Status is "Applied" or "TicketRefundPending"))
        {
            sePayTransaction.Status = "Refunded";
            sePayTransaction.RefundedAt = utcNow;
            sePayTransaction.RefundReference = request.Reference.Trim();
        }
        ticket.Payment.StatusHistories.Add(NewPaymentHistory(
            "RefundPending",
            "Refunded",
            "RefundCompleted",
            $"Mã hoàn tiền: {request.Reference.Trim()}",
            userId));
        AddAudit(session!.Booking.Court.VenueId, userId.Value, $"TicketRefunded:{sessionTicketId}:{request.Reference.Trim()}", utcNow);
        _notifications.Add(new NotificationInput(
            ticket.Player.UserId,
            NotificationTypes.Ticket,
            "Vé đã được hoàn tiền",
            $"Vé {ticket.TicketCode} đã được hoàn tiền. Mã đối soát: {request.Reference.Trim()}.",
            NotificationTones.Success,
            "/my-tickets",
            "Xem lịch sử vé"));
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishPayments([ticket.Payment], "Refunded");
        return Ok(MapTicket(ticket, includeSession: false));
    }

    public async Task<ServiceResult<SePayTransactionResponse>> CompleteAdditionalRefund(
        int? userId,
        int ticketSessionId,
        int sessionTicketId,
        int sePayTransactionId,
        CompleteTicketRefundRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                _db, transaction, $"ticket-session:{ticketSessionId}", cancellationToken))
            return Conflict(new { message = "Buổi xé vé đang được cập nhật." });
        var session = await OwnedSessionGraph(userId.Value)
            .SingleOrDefaultAsync(item => item.TicketSessionId == ticketSessionId, cancellationToken);
        var ticket = session?.Tickets.SingleOrDefault(item =>
            item.SessionTicketId == sessionTicketId);
        var sePayTransaction = ticket?.Payment.SePayTransactions.SingleOrDefault(item =>
            item.SePayTransactionId == sePayTransactionId);
        if (sePayTransaction is null)
            return NotFound(new { message = "Không tìm thấy khoản chuyển dư cần hoàn." });
        if (sePayTransaction.Status == "Refunded")
            return Ok(MapSePayTransaction(sePayTransaction));
        if (sePayTransaction.Status != "AdditionalRefundPending")
            return Conflict(new { message = "Khoản giao dịch không ở trạng thái chờ hoàn thêm." });

        var utcNow = DateTime.UtcNow;
        var reference = request.Reference.Trim();
        sePayTransaction.Status = "Refunded";
        sePayTransaction.RefundedAt = utcNow;
        sePayTransaction.RefundReference = reference;
        AddAudit(
            session!.Booking.Court.VenueId,
            userId.Value,
            $"TicketAdditionalRefunded:{sePayTransactionId}:{reference}",
            utcNow);
        _notifications.Add(new NotificationInput(
            ticket!.Player.UserId,
            NotificationTypes.Ticket,
            "Khoản chuyển dư đã được hoàn",
            $"Khoản chuyển dư {sePayTransaction.Amount:0} VND cho vé {ticket.TicketCode} đã được hoàn. Mã đối soát: {reference}.",
            NotificationTones.Success,
            "/my-tickets",
            "Xem lịch sử vé"));
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishPayments([ticket.Payment], "AdditionalRefunded");
        return Ok(MapSePayTransaction(sePayTransaction));
    }

    // Player and Staff workflows are below the shared query/mapping helpers.

    private IQueryable<TicketSession> OwnedSessionGraph(int userId, bool asTracking = true)
    {
        var query = _db.TicketSessions.Where(item => item.Booking.Court.Venue.Owner.UserId == userId);
        return SessionGraph(asTracking ? query : query.AsNoTracking());
    }

    private static IQueryable<TicketSession> SessionGraph(IQueryable<TicketSession> query) => query
        .AsSplitQuery()
        .Include(item => item.Booking).ThenInclude(item => item.StatusHistories)
        .Include(item => item.Booking).ThenInclude(item => item.Court).ThenInclude(item => item.Venue)
            .ThenInclude(item => item.Owner).ThenInclude(item => item.User)
        .Include(item => item.Tickets).ThenInclude(item => item.Payment).ThenInclude(item => item.StatusHistories)
        .Include(item => item.Tickets).ThenInclude(item => item.Payment).ThenInclude(item => item.SePayTransactions)
        .Include(item => item.Tickets).ThenInclude(item => item.Player).ThenInclude(item => item.User);

    private async Task<bool> HasCourtOverlap(
        int courtId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken,
        int? excludedBookingId = null) =>
        await _db.Bookings.AnyAsync(booking =>
            (!excludedBookingId.HasValue || booking.BookingId != excludedBookingId.Value)
            && booking.Status != "Cancelled"
            && booking.Status != "Expired"
            && (booking.Status != "Holding" || booking.HoldExpiresAt > DateTime.UtcNow)
            && (booking.Slots.Any(slot => slot.CourtId == courtId && slot.StartTime < endTime && slot.EndTime > startTime)
                || !booking.Slots.Any() && booking.CourtId == courtId
                    && booking.StartTime < endTime && booking.EndTime > startTime),
            cancellationToken);

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

    private TicketSessionParticipantsResponse MapParticipants(TicketSession session) => new()
    {
        Session = MapSession(session, DateTime.UtcNow, VietnamTime.Now),
        Tickets = session.Tickets
            .OrderBy(item => item.CreatedAt)
            .Select(item => MapTicket(item, includeSession: false))
            .ToList()
    };

    private static TicketSessionResponse MapSession(TicketSession session, DateTime utcNow, DateTime localNow)
    {
        var sold = session.Tickets.Count(item => item.Status is "Paid" or "CheckedIn");
        var reserved = session.Tickets.Count(item => item.Status == "PendingPayment" && item.HoldExpiresAt > utcNow);
        var venue = session.Booking.Court.Venue;
        return new TicketSessionResponse
        {
            TicketSessionId = session.TicketSessionId,
            BookingId = session.BookingId,
            VenueId = venue.VenueId,
            VenueName = venue.VenueName,
            VenueAddress = venue.Address,
            VenuePhone = venue.PhoneNumber,
            VenueLatitude = venue.Latitude,
            VenueLongitude = venue.Longitude,
            CourtId = session.Booking.CourtId,
            CourtNumber = session.Booking.Court.CourtNumber,
            CourtType = session.Booking.Court.CourtType,
            Title = session.Title,
            Description = session.Description,
            SkillLevel = session.SkillLevel,
            PlayFormat = session.PlayFormat,
            StartTime = session.Booking.StartTime,
            EndTime = session.Booking.EndTime,
            MaxPlayers = session.MaxPlayers,
            SoldTickets = sold,
            ReservedTickets = reserved,
            RemainingTickets = Math.Max(0, session.MaxPlayers - sold - reserved),
            TicketPrice = session.TicketPrice,
            CancellationDeadlineHours = session.CancellationDeadlineHours,
            Status = session.Status == "Published" && session.Booking.EndTime <= localNow ? "Completed" : session.Status,
            CreatedAt = AsUtc(session.CreatedAt),
            PublishedAt = AsUtc(session.PublishedAt),
            CancelledAt = AsUtc(session.CancelledAt),
            CancellationReason = session.CancellationReason
        };
    }

    private SessionTicketResponse MapTicket(SessionTicket ticket, bool includeSession)
    {
        var payment = ticket.Payment;
        return new SessionTicketResponse
        {
            SessionTicketId = ticket.SessionTicketId,
            TicketSessionId = ticket.TicketSessionId,
            PlayerId = ticket.PlayerId,
            PlayerName = ticket.Player.User.Username,
            PlayerEmail = ticket.Player.User.Email,
            TicketCode = ticket.TicketCode,
            Status = TicketingPolicy.EffectiveTicketStatus(ticket.Status, ticket.HoldExpiresAt, DateTime.UtcNow),
            CreatedAt = AsUtc(ticket.CreatedAt),
            HoldExpiresAt = AsUtc(ticket.HoldExpiresAt),
            CancelledAt = AsUtc(ticket.CancelledAt),
            CancellationReason = ticket.CancellationReason,
            CheckedInAt = AsUtc(ticket.CheckedInAt),
            CheckedInByStaffId = ticket.CheckedInByStaffId,
            PaymentId = ticket.PaymentId,
            PaymentStatus = payment.Status,
            Amount = payment.Amount,
            TransferContent = payment.TransferContent,
            BankCode = payment.BankCode,
            BankName = payment.BankName,
            BankAccountNumber = payment.BankAccountNumber,
            BankAccountName = payment.BankAccountName,
            QrImageUrl = payment.QrImageUrl,
            PaidAt = AsUtc(payment.PaidAt),
            SePayTransactions = payment.SePayTransactions
                .OrderByDescending(item => item.ReceivedAt)
                .Select(MapSePayTransaction)
                .ToList(),
            Session = includeSession ? MapSession(ticket.TicketSession, DateTime.UtcNow, VietnamTime.Now) : null
        };
    }

    private static SePayTransactionResponse MapSePayTransaction(SePayTransaction transaction) => new()
    {
        SePayTransactionId = transaction.SePayTransactionId,
        ExternalTransactionId = transaction.ExternalTransactionId,
        Amount = transaction.Amount,
        Status = transaction.Status,
        ReceivedAt = AsUtc(transaction.ReceivedAt),
        RefundedAt = AsUtc(transaction.RefundedAt),
        RefundReference = transaction.RefundReference
    };

    private static PaymentStatusHistory NewPaymentHistory(
        string? fromStatus,
        string toStatus,
        string action,
        string? reason,
        int? actorUserId) => new()
    {
        FromStatus = fromStatus,
        ToStatus = toStatus,
        Action = action,
        Reason = reason,
        ActorUserId = actorUserId,
        CreatedAt = DateTime.UtcNow
    };

    private void AddAudit(int venueId, int actorUserId, string action, DateTime timestamp) =>
        _db.VenueAuditLogs.Add(new VenueAuditLog
        {
            VenueId = venueId,
            ActorId = actorUserId,
            Action = action,
            Timestamp = timestamp
        });

    private void PublishSchedule(TicketSession session, string action) =>
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            session.Booking.Court.VenueId,
            session.Booking.CourtId,
            session.Booking.StartTime,
            session.Booking.EndTime,
            "TicketSession",
            action));

    private void PublishPayments(IEnumerable<Payment> payments, string action)
    {
        foreach (var payment in payments)
            _paymentRealtime.Publish(new PaymentChangedEvent(
                payment.PaymentId,
                payment.BookingId,
                payment.Booking.Court.VenueId,
                payment.Status,
                action));
    }

    private static string NewCode(string prefix, int maxLength) =>
        $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..maxLength].ToUpperInvariant();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;

    private static ServiceResult Ok(object? value = null) => new(ServiceResultStatus.Success, value);
    private static ServiceResult BadRequest(object? error = null) => new(ServiceResultStatus.BadRequest, Error: error);
    private static ServiceResult Unauthorized(object? error = null) => new(ServiceResultStatus.Unauthorized, Error: error);
    private static ServiceResult NotFound(object? error = null) => new(ServiceResultStatus.NotFound, Error: error);
    private static ServiceResult Conflict(object? error = null) => new(ServiceResultStatus.Conflict, Error: error);
}
