using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Payments;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Ticketing;

public sealed partial class TicketingService
{
    public async Task<ServiceResult<SessionTicketResponse>> PurchaseTicket(
        int? userId, int ticketSessionId, CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var player = await _db.Players.Include(item => item.User)
            .SingleOrDefaultAsync(item => item.UserId == userId.Value, cancellationToken);
        if (player is null) return NotFound(new { message = "Không tìm thấy hồ sơ Player." });

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                _db, transaction, $"ticket-session:{ticketSessionId}", cancellationToken)
            || !await SqlServerBookingLock.AcquireAsync(
                _db, transaction, $"player-schedule:{player.PlayerId}", cancellationToken))
            return Conflict(new { message = "Buổi xé vé đang được cập nhật. Vui lòng thử lại." });
        var session = await SessionGraph(_db.TicketSessions)
            .SingleOrDefaultAsync(item => item.TicketSessionId == ticketSessionId, cancellationToken);
        if (session is null || session.Status != "Published")
            return NotFound(new { message = "Buổi xé vé không còn mở bán." });
        if (session.Booking.StartTime <= VietnamTime.Now || session.Booking.Status != "Confirmed")
            return Conflict(new { message = "Buổi xé vé đã bắt đầu hoặc đã bị hủy." });
        if (await _playerScheduleConflict.HasConflictAsync(
                player.PlayerId,
                session.Booking.StartTime,
                session.Booking.EndTime,
                excludedBookingId: session.BookingId,
                cancellationToken: cancellationToken))
            return Conflict(new { message = "Bạn đã có lịch đặt sân, ghép trận hoặc xé vé trùng khung giờ này." });

        var utcNow = DateTime.UtcNow;
        var existing = session.Tickets.SingleOrDefault(item => item.PlayerId == player.PlayerId);
        var existingStatus = existing is null
            ? null
            : TicketingPolicy.EffectiveTicketStatus(existing.Status, existing.HoldExpiresAt, utcNow);
        if (existing is not null
            && !(existingStatus == "Expired" && existing.Payment.Status is "Pending" or "Expired"))
            return Conflict(new { message = "Bạn đã có vé hoặc lịch sử hủy/hoàn vé cho buổi này." });
        var used = session.Tickets.Count(item => item != existing
            && TicketingPolicy.OccupiesCapacity(item.Status, item.HoldExpiresAt, utcNow));
        if (used >= session.MaxPlayers) return Conflict(new { message = "Buổi xé vé đã hết chỗ." });

        var bankAccount = session.TicketPrice > 0
            ? await _db.OwnerBankAccounts.AsNoTracking().SingleOrDefaultAsync(
                item => item.OwnerId == session.Booking.Court.Venue.OwnerId && item.IsActive,
                cancellationToken)
            : null;
        if (session.TicketPrice > 0 && bankAccount is null)
            return Conflict(new { message = "Tài khoản nhận tiền của Owner hiện không khả dụng." });

        var isFree = session.TicketPrice == 0;
        var holdMinutes = Math.Clamp(_configuration.GetValue("Ticketing:PaymentHoldMinutes", 15), 1, 60);
        var holdExpiresAt = isFree ? (DateTime?)null : utcNow.AddMinutes(holdMinutes);
        var transferContent = isFree
            ? null
            : existing?.Payment.TransferContent ?? $"PLG-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        SessionTicket ticket;
        if (existing is null)
        {
            var payment = new Payment
            {
                BookingId = session.BookingId,
                Booking = session.Booking,
                PayerId = player.PlayerId,
                Payer = player,
                Amount = session.TicketPrice,
                PaymentMethod = isFree ? "Free" : "BankTransfer",
                Status = isFree ? "Paid" : "Pending",
                PaidAt = isFree ? utcNow : null,
                VerifiedAt = isFree ? utcNow : null,
                TransferCode = NewCode("TP", 40),
                TransferContent = transferContent,
                BankCode = bankAccount?.BankCode,
                BankName = bankAccount?.BankName,
                BankAccountNumber = bankAccount?.AccountNumber,
                BankAccountName = bankAccount?.AccountHolderName,
                QrImageUrl = bankAccount is null ? null : PaymentService.BuildBatchVietQrUrl(
                    bankAccount.BankCode, bankAccount.AccountNumber,
                    bankAccount.AccountHolderName, session.TicketPrice, transferContent!)
            };
            payment.StatusHistories.Add(NewPaymentHistory(
                null, payment.Status, "TicketPurchased",
                isFree ? "Vé miễn phí" : "Tạo yêu cầu thanh toán QR", userId));
            ticket = new SessionTicket
            {
                TicketSession = session,
                Player = player,
                Payment = payment,
                TicketCode = NewCode("TK", 30),
                Status = isFree ? "Paid" : "PendingPayment",
                HoldExpiresAt = holdExpiresAt,
                CreatedAt = utcNow
            };
            session.Tickets.Add(ticket);
        }
        else
        {
            ticket = existing;
            var previousPaymentStatus = ticket.Payment.Status;
            ticket.Status = isFree ? "Paid" : "PendingPayment";
            ticket.HoldExpiresAt = holdExpiresAt;
            ticket.Payment.Amount = session.TicketPrice;
            ticket.Payment.PaymentMethod = isFree ? "Free" : "BankTransfer";
            ticket.Payment.Status = isFree ? "Paid" : "Pending";
            ticket.Payment.PaidAt = isFree ? utcNow : null;
            ticket.Payment.VerifiedAt = isFree ? utcNow : null;
            ticket.Payment.VerifiedByUserId = null;
            ticket.Payment.RejectionReason = null;
            ticket.Payment.TransferCode = NewCode("TP", 40);
            ticket.Payment.TransferContent = transferContent;
            ticket.Payment.BankCode = bankAccount?.BankCode;
            ticket.Payment.BankName = bankAccount?.BankName;
            ticket.Payment.BankAccountNumber = bankAccount?.AccountNumber;
            ticket.Payment.BankAccountName = bankAccount?.AccountHolderName;
            ticket.Payment.QrImageUrl = bankAccount is null ? null : PaymentService.BuildBatchVietQrUrl(
                bankAccount.BankCode, bankAccount.AccountNumber, bankAccount.AccountHolderName,
                session.TicketPrice, transferContent!);
            ticket.Payment.StatusHistories.Add(NewPaymentHistory(
                previousPaymentStatus, ticket.Payment.Status, "TicketPaymentRetried",
                isFree ? "Chuyển lượt giữ chỗ hết hạn thành vé miễn phí"
                    : "Gia hạn thời gian giữ chỗ với mã chuyển khoản hiện có",
                userId));
        }

        AddAudit(session.Booking.Court.VenueId, userId.Value, $"TicketPurchased:{ticket.TicketCode}", utcNow);
        _notifications.Add(new NotificationInput(
            session.Booking.Court.Venue.Owner.UserId,
            NotificationTypes.Ticket,
            "Có người đăng ký buổi xé vé",
            $"{player.User.Username} vừa đăng ký buổi {session.Title}.",
            NotificationTones.Default,
            $"/owner/ticket-sessions/{session.TicketSessionId}",
            "Xem người tham gia"));
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishPayments([ticket.Payment], isFree ? "Paid" : "Created");
        return Ok(MapTicket(ticket, includeSession: true));
    }
}
