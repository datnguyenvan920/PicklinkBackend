using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Payments.Implementations;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Ticketing.Implementations;

public sealed partial class TicketingService
{
    public async Task<ServiceResult<SessionTicketResponse>> PurchaseTicket(
        int? userId, int ticketSessionId, CancellationToken cancellationToken)
    {
        if (userId is null) return Unauthorized();
        var player = await _paymentRepository.Players.Include(item => item.User)
            .SingleOrDefaultAsync(item => item.UserId == userId.Value, cancellationToken);
        if (player is null) return NotFound(new { message = "Không tìm thấy hồ sơ Player." });

        await using var transaction = await _paymentRepository.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction, $"ticket-session:{ticketSessionId}", cancellationToken)
            || !await SqlServerBookingLock.AcquireAsync(
                transaction, $"player-schedule:{player.PlayerId}", cancellationToken))
            return Conflict(new { message = "Buổi xé vé đang được cập nhật. Vui lòng thử lại." });
        var session = await SessionGraph(_paymentRepository.TicketSessions)
            .SingleOrDefaultAsync(item => item.TicketSessionId == ticketSessionId, cancellationToken);
        if (session is null || session.Status != "Published")
            return NotFound(new { message = "Buổi xé vé không còn mở bán." });
        if (session.Booking.StartTime <= VietnamTime.Now || session.Booking.Status != "Confirmed")
            return Conflict(new { message = "Buổi xé vé đã bắt đầu hoặc đã bị hủy." });
        if (!TicketingPolicy.AllowsSkillLevel(session.SkillLevel, player.SkillLevel))
            return Conflict(new { message = $"Trình độ của bạn không nằm trong khoảng Level {session.SkillLevel}." });
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
            && (item.Payment.Status == "WaitingForConfirmation"
                || TicketingPolicy.OccupiesCapacity(item.Status, item.HoldExpiresAt, utcNow)));
        if (used >= session.MaxPlayers) return Conflict(new { message = "Buổi xé vé đã hết chỗ." });

        var bankAccount = session.TicketPrice > 0
            ? await _paymentRepository.OwnerBankAccounts.AsNoTracking().SingleOrDefaultAsync(
                item => item.OwnerId == session.Booking.Court.Venue.OwnerId && item.IsActive,
                cancellationToken)
            : null;
        if (session.TicketPrice > 0 && bankAccount is null)
            return Conflict(new { message = "Tài khoản nhận tiền của Owner hiện không khả dụng." });

        var isFree = session.TicketPrice == 0;
        var holdMinutes = Math.Clamp(_configuration.GetValue("Ticketing:PaymentHoldMinutes", 5), 1, 60);
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
                TransferCode = NewCode("TP"),
                TransferContent = transferContent,
                BankCode = bankAccount?.BankCode,
                BankName = bankAccount?.BankName,
                BankAccountNumber = bankAccount?.AccountNumber,
                BankAccountName = bankAccount?.AccountHolderName,
                QrImageUrl = bankAccount is null ? null : BuildBatchVietQrUrl(
                    bankAccount.BankCode, bankAccount.AccountNumber,
                    bankAccount.AccountHolderName, session.TicketPrice, transferContent!)
            };
            payment.StatusHistories.Add(NewPaymentHistory(
                payment.PaymentId, null!, payment.Status,
                isFree ? "Vé miễn phí" : "Tạo yêu cầu thanh toán QR"));
            // Six characters, same alphabet as the court check-in code: staff type this at the door.
            var ticketCode = await CheckInCode.NextUniqueAsync(
                _paymentRepository.SessionTickets.Select(item => item.TicketCode), cancellationToken);
            ticket = new SessionTicket
            {
                TicketSession = session,
                Player = player,
                Payment = payment,
                TicketCode = ticketCode,
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
            ticket.Payment.TransferCode = NewCode("TP");
            ticket.Payment.TransferContent = transferContent;
            ticket.Payment.BankCode = bankAccount?.BankCode;
            ticket.Payment.BankName = bankAccount?.BankName;
            ticket.Payment.BankAccountNumber = bankAccount?.AccountNumber;
            ticket.Payment.BankAccountName = bankAccount?.AccountHolderName;
            ticket.Payment.QrImageUrl = bankAccount is null ? null : BuildBatchVietQrUrl(
                bankAccount.BankCode, bankAccount.AccountNumber, bankAccount.AccountHolderName,
                session.TicketPrice, transferContent!);
            ticket.Payment.StatusHistories.Add(NewPaymentHistory(
                ticket.Payment.PaymentId, previousPaymentStatus, ticket.Payment.Status,
                isFree ? "Chuyển lượt giữ chỗ hết hạn thành vé miễn phí"
                    : "Gia hạn thời gian giữ chỗ với mã chuyển khoản hiện có"));
        }

        await _paymentRepository.AddAuditLogAsync(NewAudit(session.Booking.Court.VenueId, userId.Value, $"TicketPurchased:{ticket.TicketCode}"), cancellationToken);
        _notifications.Add(new NotificationInput(
            session.Booking.Court.Venue.Owner.UserId,
            NotificationTypes.Ticket,
            "Có người đăng ký buổi xé vé",
            $"{player.User.Username} vừa đăng ký buổi {session.Title}.",
            NotificationTones.Default,
            $"/owner/ticket-sessions/{session.TicketSessionId}",
            "Xem người tham gia"));
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishPayments([ticket.Payment], isFree ? "Paid" : "Created");
        return Ok(MapTicket(ticket, utcNow, includeSession: true));
    }

    private static string BuildBatchVietQrUrl(string bankCode, string accountNumber, string accountName, decimal amount, string content)
    {
        var encodedName = Uri.EscapeDataString(accountName);
        var encodedContent = Uri.EscapeDataString(content);
        return $"https://img.vietqr.io/image/{bankCode}-{accountNumber}-compact2.png?amount={(long)amount}&addInfo={encodedContent}&accountName={encodedName}";
    }
}
