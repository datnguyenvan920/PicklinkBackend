using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Ticketing.Implementations;

public sealed partial class TicketingService
{
    public async Task<ServiceResult<PaginatedResponse<SessionTicketResponse>>> GetMyTickets(
        int? userId, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var utcNow = DateTime.UtcNow;
        var query = _paymentRepository.SessionTickets.Where(item => item.Player.UserId == userId.Value);
        var normalizedStatus = NormalizeOptional(status);
        if (normalizedStatus?.Equals("Expired", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(item => item.Status == "Expired"
                || item.Status == "PendingPayment" && item.HoldExpiresAt <= utcNow);
        else if (normalizedStatus?.Equals("PendingPayment", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(item => item.Status == "PendingPayment" && item.HoldExpiresAt > utcNow);
        else if (normalizedStatus is not null)
            query = query.Where(item => item.Status == normalizedStatus);

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var tickets = await TicketGraph(query.AsNoTrackingWithIdentityResolution())
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.SessionTicketId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(Pagination.Create(
            tickets.Select(item => MapTicket(item, utcNow, includeSession: true)),
            totalCount, page, pageSize));
    }

    public async Task<ServiceResult<SessionTicketResponse>> GetMyTicket(
        int? userId, int sessionTicketId, CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var ticket = await TicketGraph(_paymentRepository.SessionTickets.AsNoTrackingWithIdentityResolution())
            .SingleOrDefaultAsync(item => item.SessionTicketId == sessionTicketId
                && item.Player.UserId == userId.Value, cancellationToken);
        return ticket is null
            ? NotFound(new { message = "Không tìm thấy vé." })
            : Ok(MapTicket(ticket, DateTime.UtcNow, includeSession: true));
    }

    public async Task<ServiceResult<SessionTicketResponse>> CancelMyTicket(
        int? userId,
        int sessionTicketId,
        CancelSessionTicketRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var sessionId = await _paymentRepository.SessionTickets.AsNoTracking()
            .Where(item => item.SessionTicketId == sessionTicketId && item.Player.UserId == userId.Value)
            .Select(item => (int?)item.TicketSessionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (sessionId is null) return NotFound(new { message = "Không tìm thấy vé." });

        await using var transaction = await _paymentRepository.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction, $"ticket-session:{sessionId.Value}", cancellationToken))
            return Conflict(new { message = "Vé đang được cập nhật." });
        var ticket = await TicketGraph(_paymentRepository.SessionTickets)
            .SingleAsync(item => item.SessionTicketId == sessionTicketId
                && item.Player.UserId == userId.Value, cancellationToken);
        if (ticket.Status is "Cancelled" or "RefundPending" or "Refunded")
            return Ok(MapTicket(ticket, DateTime.UtcNow, includeSession: true));
        if (ticket.Status == "CheckedIn" || ticket.CheckedInAt.HasValue)
            return Conflict(new { message = "Vé đã check-in nên không thể hủy." });
        if (!TicketingPolicy.CanPlayerCancel(
                ticket.TicketSession.Booking.StartTime,
                VietnamTime.Now,
                ticket.TicketSession.CancellationDeadlineHours))
            return Conflict(new
            {
                message = $"Chỉ được hủy vé trước giờ chơi ít nhất {ticket.TicketSession.CancellationDeadlineHours} giờ."
            });

        var utcNow = DateTime.UtcNow;
        var reason = NormalizeOptional(request.Reason) ?? "Player hủy vé theo chính sách";
        var paymentFrom = ticket.Payment.Status;
        var needsRefund = ticket.Payment.Amount > 0
            && (paymentFrom is "Paid" or "WaitingForConfirmation" || ticket.Status == "Paid");
        ticket.Status = needsRefund ? "RefundPending" : "Cancelled";
        ticket.HoldExpiresAt = null;
        ticket.CancelledAt = utcNow;
        ticket.CancellationReason = reason;
        ticket.Payment.Status = needsRefund ? "RefundPending" : "Cancelled";
        ticket.Payment.StatusHistories.Add(NewPaymentHistory(
            ticket.Payment.PaymentId, paymentFrom, ticket.Payment.Status, reason));
        await _paymentRepository.AddAuditLogAsync(NewAudit(ticket.TicketSession.Booking.Court.VenueId, userId.Value,
            $"TicketCancelled:{ticket.TicketCode}"), cancellationToken);
        _notifications.Add(new NotificationInput(
            ticket.TicketSession.Booking.Court.Venue.Owner.UserId,
            NotificationTypes.Ticket,
            "Player đã hủy vé",
            needsRefund
                ? $"{ticket.Player.User.Username} đã hủy vé {ticket.TicketCode}; vé đang chờ hoàn tiền."
                : $"{ticket.Player.User.Username} đã hủy vé {ticket.TicketCode}.",
            NotificationTones.Urgent,
            $"/owner/ticket-sessions/{ticket.TicketSessionId}",
            "Xem vé"));
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishPayments([ticket.Payment], ticket.Payment.Status);
        return Ok(MapTicket(ticket, utcNow, includeSession: true));
    }
}
