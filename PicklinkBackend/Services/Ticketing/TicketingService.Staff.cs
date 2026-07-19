using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Ticketing;

public sealed partial class TicketingService
{
    public async Task<ServiceResult<PaginatedResponse<TicketSessionResponse>>> GetStaffSessions(
        int? userId, DateOnly date, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var query = _db.TicketSessions.Where(item =>
            item.Status == "Published"
            && item.Booking.StartTime >= dayStart
            && item.Booking.StartTime < dayEnd
            && item.Booking.Court.Venue.Staff.Any(staff => staff.UserId == userId.Value
                && staff.IsActive
                && ("," + staff.Permissions + ",").Contains(",CheckIn,")));
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var sessions = await SessionGraph(query.AsNoTracking())
            .OrderBy(item => item.Booking.StartTime)
            .ThenBy(item => item.Booking.Court.CourtNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(Pagination.Create(
            sessions.Select(item => MapSession(item, DateTime.UtcNow, VietnamTime.Now)),
            totalCount, page, pageSize));
    }

    public async Task<ServiceResult<StaffTicketSessionParticipantsResponse>> GetStaffParticipants(
        int? userId, int ticketSessionId, CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var session = await SessionGraph(_db.TicketSessions.AsNoTracking())
            .SingleOrDefaultAsync(item => item.TicketSessionId == ticketSessionId
                && item.Booking.Court.Venue.Staff.Any(staff => staff.UserId == userId.Value
                    && staff.IsActive
                    && ("," + staff.Permissions + ",").Contains(",CheckIn,")), cancellationToken);
        return session is null
            ? NotFound(new { message = "Không tìm thấy buổi xé vé tại sân được phân công." })
            : Ok(new StaffTicketSessionParticipantsResponse
            {
                Session = MapSession(session, DateTime.UtcNow, VietnamTime.Now),
                Tickets = session.Tickets
                    .OrderBy(item => item.CreatedAt)
                    .Select(MapStaffParticipant)
                    .ToList()
            });
    }

    public async Task<ServiceResult<StaffTicketParticipantResponse>> CheckInTicket(
        int? userId,
        CheckInSessionTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await CheckInTicketCore(userId, null, request, cancellationToken);
        return result.Status == ServiceResultStatus.Success
            ? Ok(MapStaffParticipant(result.Value!))
            : new ServiceResult<StaffTicketParticipantResponse>(result.Status, Error: result.Error);
    }

    public async Task<ServiceResult<SessionTicketResponse>> CheckInOwnerTicket(
        int? userId,
        int ticketSessionId,
        CheckInSessionTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await CheckInTicketCore(userId, ticketSessionId, request, cancellationToken);
        return result.Status == ServiceResultStatus.Success
            ? Ok(MapTicket(result.Value!, includeSession: false))
            : new ServiceResult<SessionTicketResponse>(result.Status, Error: result.Error);
    }

    private async Task<ServiceResult<SessionTicket>> CheckInTicketCore(
        int? userId,
        int? ownerTicketSessionId,
        CheckInSessionTicketRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var code = request.TicketCode.Trim().ToUpperInvariant();
        var identityQuery = _db.SessionTickets.AsNoTracking()
            .Where(item => item.TicketCode == code);
        if (ownerTicketSessionId.HasValue)
            identityQuery = identityQuery.Where(item =>
                item.TicketSessionId == ownerTicketSessionId.Value
                && item.TicketSession.Booking.Court.Venue.Owner.UserId == userId.Value);
        var ticketIdentity = await identityQuery
            .Select(item => new { item.SessionTicketId, item.TicketSessionId })
            .SingleOrDefaultAsync(cancellationToken);
        if (ticketIdentity is null) return NotFound(new { message = "Mã vé không hợp lệ." });

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                _db, transaction, $"ticket-session:{ticketIdentity.TicketSessionId}", cancellationToken))
            return Conflict(new { message = "Buổi xé vé đang được cập nhật. Vui lòng thử lại." });
        if (!await SqlServerBookingLock.AcquireAsync(
                _db, transaction, $"ticket-checkin:{ticketIdentity.SessionTicketId}", cancellationToken))
            return Conflict(new { message = "Vé đang được check-in. Vui lòng thử lại." });
        var ticket = await TicketGraph(_db.SessionTickets)
            .SingleAsync(item => item.SessionTicketId == ticketIdentity.SessionTicketId, cancellationToken);
        PicklinkBackend.Models.Staff? staff = null;
        if (ownerTicketSessionId.HasValue)
        {
            if (ticket.TicketSessionId != ownerTicketSessionId.Value
                || ticket.TicketSession.Booking.Court.Venue.Owner.UserId != userId.Value)
                return NotFound(new { message = "Vé không thuộc buổi xé vé do bạn quản lý." });
        }
        else
        {
            staff = await _db.Staff.SingleOrDefaultAsync(item => item.UserId == userId.Value
                && item.VenueId == ticket.TicketSession.Booking.Court.VenueId
                && item.IsActive
                && ("," + item.Permissions + ",").Contains(",CheckIn,"), cancellationToken);
            if (staff is null) return NotFound(new { message = "Vé không thuộc sân được phân công." });
        }
        if (ticket.Status == "CheckedIn" || ticket.CheckedInAt.HasValue)
            return Conflict(new { message = "Vé đã được check-in trước đó." });
        if (ticket.Status != "Paid" || ticket.Payment.Status != "Paid")
            return Conflict(new { message = "Chỉ vé đã thanh toán mới được check-in." });
        if (ticket.TicketSession.Status != "Published"
            || ticket.TicketSession.Booking.Status != "Confirmed")
            return Conflict(new { message = "Buổi xé vé đã bị hủy hoặc không còn hoạt động." });
        var openMinutes = Math.Clamp(
            _configuration.GetValue("Ticketing:CheckInOpenMinutes", 30), 0, 180);
        if (!TicketingPolicy.CanCheckIn(
                ticket.TicketSession.Booking.StartTime,
                ticket.TicketSession.Booking.EndTime,
                VietnamTime.Now,
                openMinutes))
            return Conflict(new
            {
                message = $"Check-in chỉ mở trước giờ chơi {openMinutes} phút đến khi buổi chơi kết thúc."
            });

        var utcNow = DateTime.UtcNow;
        ticket.Status = "CheckedIn";
        ticket.CheckedInAt = utcNow;
        ticket.CheckedInByStaffId = staff?.StaffId;
        AddAudit(ticket.TicketSession.Booking.Court.VenueId, userId.Value,
            $"TicketCheckedIn:{ticket.TicketCode}", utcNow);
        _notifications.Add(new NotificationInput(
            ticket.Player.UserId,
            NotificationTypes.Ticket,
            "Check-in thành công",
            $"Vé {ticket.TicketCode} cho buổi {ticket.TicketSession.Title} đã check-in thành công.",
            NotificationTones.Success,
            "/my-tickets",
            "Xem vé"));
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishSchedule(ticket.TicketSession, "Updated");
        return Ok(ticket);
    }

    private static StaffTicketParticipantResponse MapStaffParticipant(SessionTicket ticket) => new()
    {
        SessionTicketId = ticket.SessionTicketId,
        PlayerId = ticket.PlayerId,
        PlayerName = ticket.Player.User.Username,
        TicketCode = ticket.TicketCode,
        TicketStatus = TicketingPolicy.EffectiveTicketStatus(
            ticket.Status, ticket.HoldExpiresAt, DateTime.UtcNow),
        PaymentStatus = ticket.Payment.Status,
        PaidAt = AsUtc(ticket.Payment.PaidAt),
        CheckedInAt = AsUtc(ticket.CheckedInAt),
        CheckedInByStaffId = ticket.CheckedInByStaffId
    };

    private static IQueryable<SessionTicket> TicketGraph(IQueryable<SessionTicket> query) => query
        .AsSplitQuery()
        .Include(item => item.Payment).ThenInclude(item => item.StatusHistories)
        .Include(item => item.Payment).ThenInclude(item => item.SePayTransactions)
        .Include(item => item.Player).ThenInclude(item => item.User)
        .Include(item => item.TicketSession).ThenInclude(item => item.Tickets).ThenInclude(item => item.Payment)
        .Include(item => item.TicketSession).ThenInclude(item => item.Booking).ThenInclude(item => item.StatusHistories)
        .Include(item => item.TicketSession).ThenInclude(item => item.Booking).ThenInclude(item => item.Court)
            .ThenInclude(item => item.Venue).ThenInclude(item => item.Owner).ThenInclude(item => item.User);
}
