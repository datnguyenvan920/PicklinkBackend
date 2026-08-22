using System.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Notifications.Implementations;
using PicklinkBackend.Services.Payments;
using PicklinkBackend.Services.Schedules;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminBookingService : IAdminBookingService
{
    private static readonly string[] CancellableStatuses = ["Holding", "Confirmed"];

    private readonly IAdminRepository _adminRepository;
    private readonly NotificationService _notifications;
    private readonly PaymentRealtimeNotifier _paymentRealtime;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;

    public AdminBookingService(
        IAdminRepository adminRepository,
        NotificationService notifications,
        PaymentRealtimeNotifier paymentRealtime,
        ScheduleRealtimeNotifier scheduleRealtime)
    {
        _adminRepository = adminRepository;
        _notifications = notifications;
        _paymentRealtime = paymentRealtime;
        _scheduleRealtime = scheduleRealtime;
    }

    public async Task<PaginatedResponse<AdminBookingSummaryResponse>> ListAsync(
        string? search,
        string? status,
        string? paymentStatus,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();
        var normalizedStatus = Normalize(status);
        var normalizedPaymentStatus = Normalize(paymentStatus);

        var (items, totalCount) = await _adminRepository.GetAdminBookingListAsync(
            keyword,
            normalizedStatus,
            normalizedPaymentStatus,
            page,
            pageSize,
            cancellationToken);

        return Pagination.Create(items, totalCount, page, pageSize);
    }

    public async Task<AdminBookingCancelResult> CancelAsync(
        int bookingId,
        string reason,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _adminRepository.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        // Same lock namespace as PaymentService/StaffOperationService so an admin cancellation
        // is mutually exclusive with any concurrent payment confirmation/webhook on this booking.
        var lockAcquired = await SqlServerBookingLock.AcquireAsync(
            transaction,
            $"booking-payment:{bookingId}",
            cancellationToken);
        if (!lockAcquired)
            return AdminBookingCancelResult.Conflict("Booking đang được cập nhật. Vui lòng thử lại.");

        var booking = await _adminRepository.GetBookingForCancelByIdAsync(bookingId, cancellationToken);
        if (booking is null)
            return AdminBookingCancelResult.NotFound("Không tìm thấy booking.");

        if (!CancellableStatuses.Contains(booking.Status))
            return AdminBookingCancelResult.Conflict($"Không thể hủy booking ở trạng thái {booking.Status}.");

        var trimmedReason = reason.Trim();
        var previousStatus = booking.Status;
        booking.Status = "Cancelled";
        booking.HoldExpiresAt = null;

        var refundNeeded = false;
        foreach (var payment in booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation" or "Confirmed" or "Paid"))
        {
            var fromPaymentStatus = payment.Status;
            var needsRefund = fromPaymentStatus is "Confirmed" or "Paid";
            payment.Status = needsRefund ? "RefundPending" : "Cancelled";
            refundNeeded = refundNeeded || needsRefund;
            payment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = fromPaymentStatus,
                ToStatus = payment.Status,
                Action = "AdminCancelledBooking",
                Reason = trimmedReason,
                ActorUserId = actorUserId,
                CreatedAt = DateTime.UtcNow
            });
        }

        booking.StatusHistories.Add(new BookingStatusHistory
        {
            FromStatus = previousStatus,
            ToStatus = "Cancelled",
            Reason = $"Admin hủy booking: {trimmedReason}",
            ActorUserId = actorUserId,
            ChangedAt = DateTime.UtcNow
        });

        if (booking.Player is not null)
        {
            _notifications.Add(new NotificationInput(
                UserId: booking.Player.UserId,
                Type: NotificationTypes.Court,
                Title: "Booking đã bị hủy",
                Message: refundNeeded
                    ? $"Admin đã hủy booking {booking.BookingCode ?? $"#{booking.BookingId}"} tại {booking.Court.Venue.VenueName}: {trimmedReason}. Đội ngũ sẽ liên hệ để hoàn tiền."
                    : $"Admin đã hủy booking {booking.BookingCode ?? $"#{booking.BookingId}"} tại {booking.Court.Venue.VenueName}: {trimmedReason}.",
                Tone: NotificationTones.Urgent,
                LinkTo: "/bookings",
                LinkLabel: "Xem booking"));
        }

        _notifications.Add(new NotificationInput(
            UserId: booking.Court.Venue.Owner.UserId,
            Type: NotificationTypes.Court,
            Title: "Booking đã bị Admin hủy",
            Message: $"Admin đã hủy booking {booking.BookingCode ?? $"#{booking.BookingId}"} tại sân {booking.Court.CourtNumber}, {booking.Court.Venue.VenueName}: {trimmedReason}.",
            Tone: NotificationTones.Urgent,
            LinkTo: "/owner/bookings",
            LinkLabel: "Xem booking"));

        await _adminRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();

        if (booking.Slots.Count > 0)
        {
            foreach (var slot in booking.Slots)
                _scheduleRealtime.Publish(new ScheduleChangedEvent(booking.Court.VenueId, slot.CourtId, slot.StartTime, slot.EndTime, "Cancelled", "Deleted"));
        }
        else
        {
            _scheduleRealtime.Publish(new ScheduleChangedEvent(booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, "Cancelled", "Deleted"));
        }

        return AdminBookingCancelResult.Success(Map(booking));
    }

    public async Task<AdminBookingCancelResult> ResolveRefundDisputeAsync(
        int bookingId,
        string resolution,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _adminRepository.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction,
                $"booking-payment:{bookingId}",
                cancellationToken))
            return AdminBookingCancelResult.Conflict("Booking đang được cập nhật. Vui lòng thử lại.");

        var booking = await _adminRepository.GetBookingForCancelByIdAsync(bookingId, cancellationToken);
        if (booking is null)
            return AdminBookingCancelResult.NotFound("Không tìm thấy booking.");

        var pending = booking.Payments
            .Where(payment => payment.Status == "RefundPending" && payment.RefundDisputeStatus == "Open")
            .ToList();
        if (pending.Count == 0)
            return AdminBookingCancelResult.Conflict("Booking này không có khiếu nại hoàn tiền đang mở.");

        var trimmedResolution = resolution.Trim();
        if (trimmedResolution.Length < 5)
            return AdminBookingCancelResult.BadRequest("Kết luận khiếu nại phải có ít nhất 5 ký tự.");

        var refundRecipients = RefundRecipients(booking, pending);
        var utcNow = DateTime.UtcNow;
        foreach (var payment in pending)
        {
            payment.RefundDisputeStatus = "Resolved";
            payment.RefundDisputeResolution = trimmedResolution;
            payment.RefundDisputeResolvedAt = utcNow;
            payment.RefundDisputeResolvedByUserId = actorUserId;
            payment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = "RefundPending",
                ToStatus = "RefundPending",
                Action = "AdminResolvedRefundDispute",
                Reason = trimmedResolution,
                ActorUserId = actorUserId,
                CreatedAt = utcNow
            });
        }

        var refundAmount = pending.Sum(payment => payment.Amount);
        foreach (var recipient in refundRecipients)
        {
            _notifications.Add(new NotificationInput(
                UserId: recipient.Key,
                Type: NotificationTypes.Payment,
                Title: "Admin đã ghi nhận kết luận khiếu nại",
                Message: $"Kết luận cho khoản hoàn {recipient.Value.Amount:0} đ của booking {booking.BookingCode ?? $"#{booking.BookingId}"}: {trimmedResolution}. Hãy kiểm tra tiền thực tế trước khi xác nhận hoặc khiếu nại lại.",
                Tone: NotificationTones.Info,
                LinkTo: $"/notifications?refundPaymentId={recipient.Value.PaymentId}",
                LinkLabel: "Xem hồ sơ"));
        }

        _notifications.Add(new NotificationInput(
            UserId: booking.Court.Venue.Owner.UserId,
            Type: NotificationTypes.Payment,
            Title: "Admin đã ghi nhận kết luận khiếu nại",
            Message: $"Khiếu nại khoản hoàn {refundAmount:0} đ của booking {booking.BookingCode ?? $"#{booking.BookingId}"} đã có kết luận: {trimmedResolution}.",
            Tone: NotificationTones.Info,
            LinkTo: booking.MatchId.HasValue ? "/owner/match-bookings" : "/owner/bookings",
            LinkLabel: "Xem booking"));

        await _adminRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        foreach (var payment in pending)
        {
            _paymentRealtime.Publish(new PaymentChangedEvent(
                payment.PaymentId,
                payment.BookingId,
                booking.Court.VenueId,
                payment.Status,
                "AdminResolvedRefundDispute"));
        }

        return AdminBookingCancelResult.Success(Map(booking));
    }

    private static AdminBookingSummaryResponse Map(Booking booking)
    {
        var pendingRefunds = booking.Payments
            .Where(payment => payment.Status == "RefundPending")
            .ToList();
        var dispute = pendingRefunds
            .Where(payment => payment.RefundDisputeStatus == "Open")
            .OrderByDescending(payment => payment.RefundDisputedAt)
            .FirstOrDefault()
            ?? pendingRefunds
                .Where(payment => payment.RefundDisputeStatus != null)
                .OrderByDescending(payment => payment.RefundDisputedAt)
                .FirstOrDefault();
        var proofPayment = pendingRefunds
            .Where(payment => !string.IsNullOrWhiteSpace(payment.RefundProofImageUrl))
            .OrderByDescending(payment => payment.RefundProofSubmittedAt)
            .FirstOrDefault();
        var latestByCreated = booking.Payments
            .OrderByDescending(payment => payment.SubmittedAt ?? payment.PaidAt ?? DateTime.MinValue)
            .FirstOrDefault();
        var latestByVerified = booking.Payments
            .OrderByDescending(payment => payment.VerifiedAt ?? payment.PaidAt ?? DateTime.MinValue)
            .FirstOrDefault();

        return new AdminBookingSummaryResponse
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode,
            Status = booking.Status,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            CreatedAt = booking.CreatedAt,
            TotalAmount = booking.TotalAmount,
            CourtAmount = booking.CourtAmount,
            VenueId = booking.Court.VenueId,
            VenueName = booking.Court.Venue.VenueName,
            CourtId = booking.CourtId,
            CourtNumber = booking.Court.CourtNumber,
            OwnerName = booking.Court.Venue.Owner.User.Username,
            OwnerEmail = booking.Court.Venue.Owner.User.Email,
            PlayerName = booking.Player?.User.Username ?? "Owner tạo lịch",
            PlayerEmail = booking.Player?.User.Email,
            PaymentStatus = pendingRefunds.Any(payment => payment.RefundDisputeStatus == "Open")
                ? "RefundDisputed"
                : pendingRefunds.Count > 0
                    ? "RefundPending"
                : booking.Payments.Any(payment => payment.Status == "Refunded")
                    ? "Refunded"
                    : latestByCreated?.Status ?? "NoPayment",
            PaymentMethod = latestByCreated?.PaymentMethod,
            PaymentSubmittedAt = latestByCreated?.SubmittedAt,
            PaymentVerifiedAt = latestByVerified?.VerifiedAt,
            RefundAmount = pendingRefunds.Sum(payment => payment.Amount),
            RefundPendingSince = pendingRefunds
                .SelectMany(payment => payment.StatusHistories)
                .Where(history => history.ToStatus == "RefundPending")
                .Select(history => (DateTime?)history.CreatedAt)
                .Min(),
            RefundProofPaymentId = proofPayment?.PaymentId,
            RefundProofImageUrl = proofPayment is null
                ? null
                : $"/api/payments/{proofPayment.PaymentId}/refund/proof-file",
            RefundReference = proofPayment?.RefundReference,
            RefundProofSubmittedAt = proofPayment?.RefundProofSubmittedAt,
            RefundDisputeStatus = dispute?.RefundDisputeStatus,
            RefundDisputeReason = dispute?.RefundDisputeReason,
            RefundDisputedAt = dispute?.RefundDisputedAt,
            RefundDisputeResolution = dispute?.RefundDisputeResolution,
            RefundDisputeResolvedAt = dispute?.RefundDisputeResolvedAt
        };
    }

    private static Dictionary<int, (decimal Amount, int PaymentId)> RefundRecipients(Booking booking, IEnumerable<Payment> payments)
    {
        var matchPlayerUsers = booking.Match?.MatchParticipants
            .ToDictionary(participant => participant.PlayerId, participant => participant.Player.UserId)
            ?? new Dictionary<int, int>();

        return payments
            .Select(payment =>
            {
                var recipientPlayerId = payment.ClaimedByPlayerId ?? payment.PayerId;
                return new
                {
                    UserId = matchPlayerUsers.GetValueOrDefault(recipientPlayerId, payment.Payer.UserId),
                    payment.Amount,
                    payment.PaymentId
                };
            })
            .Where(recipient => recipient.UserId > 0)
            .GroupBy(recipient => recipient.UserId)
            .ToDictionary(
                group => group.Key,
                group => (group.Sum(recipient => recipient.Amount), group.First().PaymentId));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}
