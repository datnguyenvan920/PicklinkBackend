using System.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Notifications.Implementations;
using PicklinkBackend.Services.Schedules;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminBookingService : IAdminBookingService
{
    private static readonly string[] CancellableStatuses = ["Holding", "Confirmed"];

    private readonly IAdminRepository _adminRepository;
    private readonly NotificationService _notifications;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;

    public AdminBookingService(
        IAdminRepository adminRepository,
        NotificationService notifications,
        ScheduleRealtimeNotifier scheduleRealtime)
    {
        _adminRepository = adminRepository;
        _notifications = notifications;
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

    private static AdminBookingSummaryResponse Map(Booking booking)
    {
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
            PaymentStatus = latestByCreated?.Status ?? "NoPayment",
            PaymentMethod = latestByCreated?.PaymentMethod,
            PaymentSubmittedAt = latestByCreated?.SubmittedAt,
            PaymentVerifiedAt = latestByVerified?.VerifiedAt
        };
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}
