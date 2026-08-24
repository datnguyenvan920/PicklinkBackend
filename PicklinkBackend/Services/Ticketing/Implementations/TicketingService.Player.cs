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
            query = query.Where(item => item.Status == "PendingPayment"
                && (item.Payment.Status == "WaitingForConfirmation" || item.HoldExpiresAt > utcNow));
        else if (normalizedStatus is not null)
            query = query.Where(item => item.Status == normalizedStatus);
        else
            // Default (unfiltered) history only records tickets that were ever paid for —
            // holds that expired without payment are not a real transaction.
            query = query.Where(item => item.Status != "Expired"
                && !(item.Status == "PendingPayment" && item.HoldExpiresAt <= utcNow));

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
        if (ticket is null) return NotFound(new { message = "Không tìm thấy vé." });

        // The player's ticket screen polls this endpoint while payment is pending, so piggyback
        // a throttled SePay lookup here instead of waiting solely on the inbound webhook.
        if (ticket.Status == "PendingPayment"
            && ticket.Payment.Status is "Pending" or "WaitingForConfirmation"
            && !string.IsNullOrWhiteSpace(ticket.Payment.TransferContent)
            && await _sePayReconciliation.TryReconcileAsync(ticket.Payment.TransferContent, cancellationToken))
        {
            ticket = await TicketGraph(_paymentRepository.SessionTickets.AsNoTrackingWithIdentityResolution())
                .SingleAsync(item => item.SessionTicketId == sessionTicketId
                    && item.Player.UserId == userId.Value, cancellationToken);
        }

        return Ok(MapTicket(ticket, DateTime.UtcNow, includeSession: true));
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
        if (ticket.Status is "Cancelled" or "Expired" or "RefundPending" or "Refunded")
            return Ok(MapTicket(ticket, DateTime.UtcNow, includeSession: true));
        if (ticket.Status == "CheckedIn" || ticket.CheckedInAt.HasValue)
            return Conflict(new { message = "Vé đã check-in nên không thể hủy." });
        // An unpaid hold represents no commitment yet, so it can always be released
        // immediately — the cancellation deadline only protects a paid or
        // under-review commitment from a last-minute pullout.
        var hasPaymentCommitment = ticket.Payment.Status is not "Pending";
        if (hasPaymentCommitment && !TicketingPolicy.CanPlayerCancel(
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
        var isPaid = paymentFrom == "Paid" || ticket.Status == "Paid";
        var releaseStatus = isPaid ? "Cancelled" : "Expired";
        ticket.Status = releaseStatus;
        ticket.HoldExpiresAt = null;
        ticket.CancelledAt = isPaid ? utcNow : null;
        ticket.CancellationReason = reason;
        var paymentChanged = paymentFrom is "Pending" or "WaitingForConfirmation";
        if (paymentChanged)
        {
            ticket.Payment.Status = releaseStatus;
            ticket.Payment.StatusHistories.Add(NewPaymentHistory(
                ticket.Payment.PaymentId, paymentFrom, ticket.Payment.Status, reason));
        }
        await _paymentRepository.AddAuditLogAsync(NewAudit(ticket.TicketSession.Booking.Court.VenueId, userId.Value,
            $"TicketCancelled:{ticket.TicketCode}"), cancellationToken);
        _notifications.Add(new NotificationInput(
            ticket.TicketSession.Booking.Court.Venue.Owner.UserId,
            NotificationTypes.Ticket,
            "Player đã hủy vé",
            isPaid
                ? $"{ticket.Player.User.Username} đã hủy vé {ticket.TicketCode}; khoản đã thanh toán không được hoàn lại."
                : $"{ticket.Player.User.Username} đã hủy vé {ticket.TicketCode}.",
            NotificationTones.Urgent,
            $"/owner/ticket-sessions/{ticket.TicketSessionId}",
            "Xem vé"));
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        if (paymentChanged) PublishPayments([ticket.Payment], ticket.Payment.Status);
        PublishSchedule(ticket.TicketSession, "Updated");
        return Ok(MapTicket(ticket, utcNow, includeSession: true));
    }
}
