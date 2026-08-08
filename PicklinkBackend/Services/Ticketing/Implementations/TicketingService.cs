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
    private readonly IPaymentRepository _paymentRepository;
    private readonly IConfiguration _configuration;
    private readonly PlayerScheduleConflictService _playerScheduleConflict;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly PaymentRealtimeNotifier _paymentRealtime;
    private readonly NotificationService _notifications;

    public TicketingService(
        IPaymentRepository paymentRepository,
        IConfiguration configuration,
        PlayerScheduleConflictService playerScheduleConflict,
        ScheduleRealtimeNotifier scheduleRealtime,
        PaymentRealtimeNotifier paymentRealtime,
        NotificationService notifications)
    {
        _paymentRepository = paymentRepository;
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
        var now = DateTime.UtcNow;
        var query = _paymentRepository.TicketSessions.AsNoTracking()
            .AsSingleQuery()
            .Include(session => session.Booking).ThenInclude(booking => booking.Court).ThenInclude(court => court.Venue)
            .Include(session => session.Tickets)
            .Where(session => session.Status == "Published" && session.Booking.StartTime > now);

        if (venueId.HasValue) query = query.Where(session => session.Booking.Court.VenueId == venueId.Value);
        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);
            query = query.Where(session => session.Booking.StartTime >= start && session.Booking.StartTime < end);
        }
        if (!string.IsNullOrWhiteSpace(skillLevel))
            query = query.Where(session => session.SkillLevel == skillLevel);
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

        var sessions = await query.OrderBy(session => session.Booking.StartTime).ToListAsync(cancellationToken);
        if (onlyAvailable)
        {
            sessions = sessions.Where(session => AvailableTicketsCount(session, now) > 0).ToList();
        }

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = sessions.Count;
        var pagedItems = sessions
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(session => MapSession(session, now))
            .ToList();

        return Ok(Pagination.Create(pagedItems, totalCount, page, pageSize));
    }

    public async Task<ServiceResult<TicketSessionResponse>> GetPublishedSession(
        int ticketSessionId,
        CancellationToken cancellationToken)
    {
        var session = await LoadSessionReadAsync(ticketSessionId, cancellationToken);
        if (session is null || session.Status != "Published")
            return NotFound(new { message = "Không tìm thấy phiên vé." });

        return Ok(MapSession(session, DateTime.UtcNow));
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
            .Include(session => session.Tickets)
            .Where(session => session.Booking.Court.Venue.Owner.UserId == userId.Value);

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(session => session.Status == status);

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var items = await query
            .OrderByDescending(session => session.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(Pagination.Create(items.Select(item => MapSession(item, now)).ToList(), totalCount, page, pageSize));
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

        return Ok(MapSession(session, DateTime.UtcNow));
    }

    public async Task<ServiceResult<TicketSessionResponse>> CreateSession(
        int? userId,
        CreateTicketSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        if (request.TotalTickets < 1)
            return BadRequest(new { message = "Số lượng vé phải từ 1 trở lên." });
        if (request.TicketPrice <= 0)
            return BadRequest(new { message = "Giá vé phải lớn hơn 0." });

        var booking = await _paymentRepository.Bookings
            .Include(b => b.Court).ThenInclude(c => c.Venue).ThenInclude(v => v.Owner)
            .SingleOrDefaultAsync(b => b.BookingId == request.BookingId, cancellationToken);
        if (booking is null || booking.Court.Venue.Owner.UserId != userId.Value)
            return NotFound(new { message = "Không tìm thấy đơn đặt sân." });
        if (booking.Status != "Confirmed")
            return BadRequest(new { message = "Chỉ có thể tạo phiên vé cho đơn đặt sân đã xác nhận." });
        if (booking.MatchId.HasValue)
            return BadRequest(new { message = "Đơn đặt sân dành cho ghép trận không thể phát hành vé lẻ." });

        if (await _paymentRepository.TicketSessions.AnyAsync(s => s.BookingId == booking.BookingId, cancellationToken))
            return Conflict(new { message = "Đơn đặt sân này đã có phiên vé được tạo." });

        var session = new TicketSession
        {
            BookingId = booking.BookingId,
            Title = request.Title.Trim(),
            Description = NormalizeOptional(request.Description),
            SkillLevel = NormalizeOptional(request.SkillLevel) ?? "Tất cả trình độ",
            PlayFormat = NormalizeOptional(request.PlayFormat) ?? "Giao lưu tự do",
            TotalTickets = request.TotalTickets,
            TicketPrice = request.TicketPrice,
            Status = "Published",
            CreatedAt = DateTime.UtcNow
        };

        await _paymentRepository.AddTicketSessionAsync(session, cancellationToken);
        await _paymentRepository.AddAuditLogAsync(NewAudit(booking.Court.VenueId, userId.Value, $"CreatedTicketSession:{session.TicketSessionId}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);

        var loaded = await LoadSessionReadAsync(session.TicketSessionId, cancellationToken);
        return Ok(MapSession(loaded!, DateTime.UtcNow));
    }

    public async Task<ServiceResult<TicketSessionResponse>> UpdateSession(
        int? userId,
        int ticketSessionId,
        UpdateTicketSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();

        var session = await LoadSessionTrackedAsync(ticketSessionId, cancellationToken);
        if (session is null || session.Booking.Court.Venue.Owner.UserId != userId.Value)
            return NotFound(new { message = "Không tìm thấy phiên vé." });

        var now = DateTime.UtcNow;
        var activeCount = SoldOrReservedTicketsCount(session, now);
        if (request.TotalTickets < activeCount)
            return BadRequest(new { message = $"Số lượng vé không thể nhỏ hơn số vé đã được bán/giữ ({activeCount})." });
        if (request.TicketPrice <= 0)
            return BadRequest(new { message = "Giá vé phải lớn hơn 0." });

        session.Title = request.Title.Trim();
        session.Description = NormalizeOptional(request.Description);
        session.SkillLevel = NormalizeOptional(request.SkillLevel) ?? "Tất cả trình độ";
        session.PlayFormat = NormalizeOptional(request.PlayFormat) ?? "Giao lưu tự do";
        session.TotalTickets = request.TotalTickets ?? session.TotalTickets;
        session.TicketPrice = request.TicketPrice;

        await _paymentRepository.AddAuditLogAsync(NewAudit(session.Booking.Court.VenueId, userId.Value, $"UpdatedTicketSession:{ticketSessionId}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);

        var loaded = await LoadSessionReadAsync(ticketSessionId, cancellationToken);
        return Ok(MapSession(loaded!, now));
    }

    public async Task<ServiceResult<TicketSessionResponse>> PublishSession(int? userId, int ticketSessionId, CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var session = await LoadSessionTrackedAsync(ticketSessionId, cancellationToken);
        if (session is null || session.Booking.Court.Venue.Owner.UserId != userId.Value)
            return NotFound(new { message = "Không tìm thấy phiên vé." });

        session.Status = "Published";
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        return Ok(MapSession(session, DateTime.UtcNow));
    }

    public async Task<ServiceResult<TicketSessionResponse>> CancelSession(int? userId, int ticketSessionId, CancelTicketSessionRequest request, CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var session = await LoadSessionTrackedAsync(ticketSessionId, cancellationToken);
        if (session is null || session.Booking.Court.Venue.Owner.UserId != userId.Value)
            return NotFound(new { message = "Không tìm thấy phiên vé." });

        session.Status = "Cancelled";
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        return Ok(MapSession(session, DateTime.UtcNow));
    }

    public async Task<ServiceResult<TicketSessionParticipantsResponse>> GetOwnerParticipants(int? userId, int ticketSessionId, CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var session = await LoadSessionReadAsync(ticketSessionId, cancellationToken);
        if (session is null || session.Booking.Court.Venue.Owner.UserId != userId.Value)
            return NotFound(new { message = "Không tìm thấy phiên vé." });

        var response = new TicketSessionParticipantsResponse
        {
            Session = MapSession(session, DateTime.UtcNow),
            Tickets = session.Tickets.Select(t => MapTicket(t, DateTime.UtcNow)).ToList()
        };
        return Ok(response);
    }

    public Task<ServiceResult<SessionTicketResponse>> CompleteRefund(int? userId, int ticketSessionId, int sessionTicketId, CompleteTicketRefundRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<ServiceResult<SessionTicketResponse>>(Ok(new SessionTicketResponse()));
    }

    public Task<ServiceResult<SePayTransactionResponse>> CompleteAdditionalRefund(int? userId, int ticketSessionId, int sessionTicketId, int sePayTransactionId, CompleteTicketRefundRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<ServiceResult<SePayTransactionResponse>>(Ok(new SePayTransactionResponse()));
    }

    public async Task<ServiceResult<TicketSessionResponse>> CloseSession(
        int sessionId,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();

        var session = await LoadSessionTrackedAsync(sessionId, cancellationToken);
        if (session is null || session.Booking.Court.Venue.Owner.UserId != userId.Value)
            return NotFound(new { message = "Không tìm thấy phiên vé." });

        session.Status = "Closed";
        await _paymentRepository.AddAuditLogAsync(NewAudit(session.Booking.Court.VenueId, userId.Value, $"ClosedTicketSession:{sessionId}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);

        var loaded = await LoadSessionReadAsync(sessionId, cancellationToken);
        return Ok(MapSession(loaded!, DateTime.UtcNow));
    }

    private Task<TicketSession?> LoadSessionReadAsync(int sessionId, CancellationToken cancellationToken) =>
        _paymentRepository.TicketSessions.AsNoTracking()
            .AsSingleQuery()
            .Include(s => s.Booking).ThenInclude(b => b.Court).ThenInclude(c => c.Venue).ThenInclude(v => v.Owner)
            .Include(s => s.Tickets).ThenInclude(t => t.Player).ThenInclude(p => p.User)
            .Include(s => s.Tickets).ThenInclude(t => t.Payment)
            .SingleOrDefaultAsync(s => s.TicketSessionId == sessionId, cancellationToken);

    private Task<TicketSession?> LoadSessionTrackedAsync(int sessionId, CancellationToken cancellationToken) =>
        _paymentRepository.TicketSessions
            .AsSingleQuery()
            .Include(s => s.Booking).ThenInclude(b => b.Court).ThenInclude(c => c.Venue).ThenInclude(v => v.Owner)
            .Include(s => s.Tickets).ThenInclude(t => t.Player).ThenInclude(p => p.User)
            .Include(s => s.Tickets).ThenInclude(t => t.Payment)
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
            (ticket.Status == "PendingPayment" && ticket.HoldExpiresAt > now));
    }

    private static TicketSessionResponse MapSession(TicketSession session, DateTime now, DateTime? localNow = null)
    {
        var available = AvailableTicketsCount(session, now);
        return new TicketSessionResponse
        {
            TicketSessionId = session.TicketSessionId,
            BookingId = session.BookingId,
            VenueId = session.Booking.Court.VenueId,
            VenueName = session.Booking.Court.Venue.VenueName,
            VenueAddress = session.Booking.Court.Venue.Address,
            CourtNumber = session.Booking.Court.CourtNumber,
            Title = session.Title,
            Description = session.Description,
            SkillLevel = session.SkillLevel,
            PlayFormat = session.PlayFormat,
            TotalTickets = session.TotalTickets,
            AvailableTickets = available,
            SoldTickets = session.Tickets.Count(t => t.Status is "Paid" or "CheckedIn"),
            TicketPrice = session.TicketPrice,
            Status = session.Status,
            StartTime = session.Booking.StartTime,
            EndTime = session.Booking.EndTime,
            CreatedAt = session.CreatedAt,
            Tickets = session.Tickets.OrderByDescending(t => t.CreatedAt).Select(t => MapTicket(t, now)).ToList()
        };
    }

    private static SessionTicketResponse MapTicket(SessionTicket ticket, DateTime now, bool includeSession = true) => new()
    {
        SessionTicketId = ticket.SessionTicketId,
        TicketSessionId = ticket.TicketSessionId,
        TicketCode = ticket.TicketCode,
        PlayerId = ticket.PlayerId,
        PlayerName = ticket.Player.User.Username,
        PlayerProfileImageUrl = ticket.Player.User.ProfileImageUrl,
        Status = ticket.Status,
        Amount = ticket.Payment.Amount,
        PaymentStatus = ticket.Payment.Status,
        TransferCode = ticket.Payment.TransferCode,
        BankName = ticket.Payment.BankName,
        BankAccountNumber = ticket.Payment.BankAccountNumber,
        BankAccountName = ticket.Payment.BankAccountName,
        HoldExpiresAt = ticket.HoldExpiresAt,
        HoldRemainingSeconds = ticket.HoldExpiresAt.HasValue && ticket.HoldExpiresAt.Value > now
            ? (int)Math.Ceiling((ticket.HoldExpiresAt.Value - now).TotalSeconds)
            : null,
        CheckedInAt = ticket.CheckedInAt,
        CreatedAt = ticket.CreatedAt,
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
