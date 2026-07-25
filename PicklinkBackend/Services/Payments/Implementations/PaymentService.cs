using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Matches;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Notifications.Implementations;
using PicklinkBackend.Services.Payments;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Payments.Implementations;

public sealed record PaymentServiceDependencies(
    IPaymentRepository PaymentRepository,
    IWebHostEnvironment Environment,
    IConfiguration Configuration,
    ScheduleRealtimeNotifier ScheduleRealtime,
    PaymentRealtimeNotifier PaymentRealtime,
    MatchRealtimeNotifier MatchRealtime,
    NotificationService Notifications);

public class PaymentService : IPaymentService
{
    private static readonly HashSet<string> AllowedReceiptTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    private readonly IPaymentRepository _paymentRepository;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly PaymentRealtimeNotifier _paymentRealtime;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private readonly NotificationService _notifications;
    private int? _currentUserId;

    private PaymentService(
        IPaymentRepository paymentRepository,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ScheduleRealtimeNotifier scheduleRealtime,
        PaymentRealtimeNotifier paymentRealtime,
        MatchRealtimeNotifier matchRealtime,
        NotificationService notifications)
    {
        _paymentRepository = paymentRepository;
        _environment = environment;
        _configuration = configuration;
        _scheduleRealtime = scheduleRealtime;
        _paymentRealtime = paymentRealtime;
        _matchRealtime = matchRealtime;
        _notifications = notifications;
    }

    public PaymentService(PaymentServiceDependencies dependencies)
        : this(
            dependencies.PaymentRepository,
            dependencies.Environment,
            dependencies.Configuration,
            dependencies.ScheduleRealtime,
            dependencies.PaymentRealtime,
            dependencies.MatchRealtime,
            dependencies.Notifications)
    {
    }

    public void SetCurrentUserId(int? userId)
    {
        _currentUserId = userId;
    }

    private static ServiceResult Ok(object? value = null) =>
        new(ServiceResultStatus.Success, value);

    private static ServiceResult NoContent() =>
        new(ServiceResultStatus.NoContent);

    private static ServiceResult BadRequest(object? error = null) =>
        new(ServiceResultStatus.BadRequest, Error: error);

    private static ServiceResult Unauthorized(object? error = null) =>
        new(ServiceResultStatus.Unauthorized, Error: error);

    private static ServiceResult Forbid(object? error = null) =>
        new(ServiceResultStatus.Forbidden, Error: error);

    private static ServiceResult NotFound(object? error = null) =>
        new(ServiceResultStatus.NotFound, Error: error);

    private static ServiceResult Conflict(object? error = null) =>
        new(ServiceResultStatus.Conflict, Error: error);

    private static ServiceResult StatusCode(int statusCode, object? body = null) =>
        statusCode >= 400
            ? new(ServiceResultStatus.StatusCode, Error: body, RawStatusCode: statusCode)
            : new(ServiceResultStatus.StatusCode, Value: body, RawStatusCode: statusCode);

    public async Task<ServiceResult<OwnerBankAccountResponse>> GetBankAccount(CancellationToken cancellationToken)
    {
        var owner = await CurrentOwnerAsync(cancellationToken);
        if (owner is null) return Forbid();
        var account = await _paymentRepository.OwnerBankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == owner.OwnerId, cancellationToken);
        return account is null ? NotFound(new { message = "Chủ sân chưa cấu hình tài khoản nhận tiền." }) : Ok(MapAccount(account));
    }

    public async Task<ServiceResult<OwnerBankAccountResponse>> UpsertBankAccount(
        OwnerBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        var owner = await CurrentOwnerAsync(cancellationToken);
        if (owner is null) return Forbid();
        var account = await _paymentRepository.OwnerBankAccounts.SingleOrDefaultAsync(item => item.OwnerId == owner.OwnerId, cancellationToken);
        if (account is null)
        {
            account = new OwnerBankAccount { OwnerId = owner.OwnerId, CreatedAt = DateTime.UtcNow };
            await _paymentRepository.AddOwnerBankAccountAsync(account, cancellationToken);
        }

        account.BankCode = request.BankCode.Trim().ToUpperInvariant();
        account.BankName = request.BankName.Trim();
        account.AccountNumber = request.AccountNumber.Trim();
        account.AccountHolderName = request.AccountHolderName.Trim().ToUpperInvariant();
        account.IsActive = true;
        account.UpdatedAt = DateTime.UtcNow;
        foreach (var venueId in await _paymentRepository.Venues.Where(item => item.OwnerId == owner.OwnerId).Select(item => item.VenueId).ToListAsync(cancellationToken))
            await _paymentRepository.AddAuditLogAsync(NewAudit(venueId, "BankAccountUpdated"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        return Ok(MapAccount(account));
    }

    public async Task<ServiceResult<BatchPaymentPreviewResponse>> PreviewBatchTransfer(
        int bookingId,
        BatchPaymentPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (request.PayerIds.Count == 0 || request.PayerIds.Distinct().Count() != request.PayerIds.Count)
            return BadRequest(new { message = "Danh sách thành viên thanh toán không hợp lệ." });

        var currentPlayerId = await _paymentRepository.Players
            .Where(item => item.UserId == userId.Value)
            .Select(item => (int?)item.PlayerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (currentPlayerId is null) return Forbid();

        var booking = await BatchPaymentBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null || booking.Match is null)
            return NotFound(new { message = "Không tìm thấy booking của trận đấu." });

        if (RebalancePendingMatchPayments(booking))
            await _paymentRepository.SaveChangesAsync(cancellationToken);

        var approvedParticipantIds = booking.Match.MatchParticipants
            .Where(IsApprovedMatchParticipant)
            .Select(item => item.PlayerId)
            .ToHashSet();
        var currentParticipantIsApproved = approvedParticipantIds.Contains(currentPlayerId.Value);
        var targetParticipantIds = request.PayerIds.ToHashSet();
        if (!currentParticipantIsApproved || !targetParticipantIds.SetEquals(targetParticipantIds.Intersect(approvedParticipantIds)))
            return Forbid();

        var payments = booking.Payments
            .Where(item => targetParticipantIds.Contains(item.PayerId))
            .OrderBy(item => item.PayerId)
            .ToList();
        if (payments.Count != targetParticipantIds.Count)
            return NotFound(new { message = "Không tìm thấy đầy đủ khoản thanh toán đã chọn." });
        if (booking.Status != "Holding" || booking.HoldExpiresAt <= DateTime.UtcNow)
            return Conflict(new { message = "Booking không còn trong thời gian giữ chỗ." });
        if (payments.Any(item => item.Status != "Pending"))
            return Conflict(new { message = "Một hoặc nhiều phần đã được gửi hoặc thanh toán. Vui lòng tải lại." });
        if (!HasOneConfiguredBankAccount(payments))
            return Conflict(new { message = "Các khoản đã chọn không có cùng tài khoản nhận tiền hợp lệ." });

        var transferContent = BuildBatchTransferContent(booking, targetParticipantIds);
        var totalAmount = payments.Sum(item => item.Amount);
        return Ok(new BatchPaymentPreviewResponse
        {
            BookingId = booking.BookingId,
            PayerIds = payments.Select(item => item.PayerId).ToList(),
            MemberNames = payments.Select(item => item.Payer.User.Username).ToList(),
            TotalAmount = totalAmount,
            TransferContent = transferContent,
            QrImageUrl = BuildBatchVietQrUrl(
                payments[0].BankCode!,
                payments[0].BankAccountNumber!,
                payments[0].BankAccountName!,
                totalAmount,
                transferContent)
        });
    }

    public async Task<ServiceResult<BatchPaymentResponse>> SubmitBatchTransfer(
        int bookingId,
        SubmitBatchPaymentReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var receipt = request.Receipt;
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (request.PayerIds.Count == 0 || request.PayerIds.Distinct().Count() != request.PayerIds.Count)
            return BadRequest(new { message = "Danh sách thành viên thanh toán không hợp lệ." });
        if (receipt is null || receipt.Length == 0)
            return BadRequest(new { message = "Vui lòng tải ảnh biên lai." });
        if (receipt.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "Ảnh biên lai không được vượt quá 5 MB." });
        if (!AllowedReceiptTypes.Contains(receipt.ContentType))
            return BadRequest(new { message = "Biên lai chỉ hỗ trợ JPG, PNG hoặc WEBP." });

        if (!await ImageUploadPolicy.HasValidSignatureAsync(receipt, cancellationToken))
            return BadRequest(new { message = "Nội dung tệp biên lai không khớp với định dạng ảnh." });

        var currentPlayerId = await _paymentRepository.Players
            .Where(item => item.UserId == userId.Value)
            .Select(item => (int?)item.PlayerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (currentPlayerId is null) return Forbid();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await BatchPaymentBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null || booking.Match is null)
            return NotFound(new { message = "Không tìm thấy booking của trận đấu." });

        if (RebalancePendingMatchPayments(booking))
            await _paymentRepository.SaveChangesAsync(cancellationToken);

        var approvedParticipantIds = booking.Match.MatchParticipants
            .Where(IsApprovedMatchParticipant)
            .Select(item => item.PlayerId)
            .ToHashSet();
        var currentParticipantIsApproved = approvedParticipantIds.Contains(currentPlayerId.Value);
        var targetParticipantIds = request.PayerIds.ToHashSet();
        if (!currentParticipantIsApproved || !targetParticipantIds.SetEquals(targetParticipantIds.Intersect(approvedParticipantIds)))
            return Forbid();

        var payments = booking.Payments
            .Where(item => targetParticipantIds.Contains(item.PayerId))
            .OrderBy(item => item.PayerId)
            .ToList();
        if (payments.Count != targetParticipantIds.Count)
            return NotFound(new { message = "Không tìm thấy đầy đủ khoản thanh toán đã chọn." });
        if (booking.Status != "Holding" || booking.HoldExpiresAt <= DateTime.UtcNow)
            return Conflict(new { message = "Booking không còn trong thời gian giữ chỗ." });
        if (payments.Any(item => item.Status != "Pending"))
            return Conflict(new { message = "Một hoặc nhiều phần đã được gửi hoặc thanh toán. Vui lòng tải lại." });
        if (!HasOneConfiguredBankAccount(payments))
            return Conflict(new { message = "Các khoản đã chọn không có cùng tài khoản nhận tiền hợp lệ." });

        var now = DateTime.UtcNow;
        var transferContent = BuildBatchTransferContent(booking, targetParticipantIds);
        var receiptUrl = await SaveReceiptAsync(booking.BookingId, receipt, cancellationToken);
        var newGroupId = Guid.NewGuid();

        foreach (var payment in payments)
        {
            var previous = payment.Status;
            payment.Status = "WaitingForConfirmation";
            payment.SubmittedAt = now;
            payment.PaymentMethod = "BankTransfer";
            payment.TransferCode = transferContent;
            payment.TransferContent = transferContent;
            payment.ReceiptImageUrl = receiptUrl;
            payment.RejectionReason = null;
            payment.PaymentGroupId = newGroupId;
            payment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = previous,
                ToStatus = "WaitingForConfirmation",
                Action = "BatchTransferSubmitted",
                Reason = $"Thành viên gửi biên lai chuyển khoản gộp cho {payments.Count} phần.",
                ActorUserId = userId.Value,
                CreatedAt = now
            });
        }

        await _paymentRepository.AddAuditLogAsync(NewAudit(booking.Court.VenueId, $"BatchPaymentSubmitted:{booking.BookingId}:{payments.Count}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _notifications.Add(new NotificationInput(
            UserId: booking.Court.Venue.Owner.UserId,
            Type: NotificationTypes.Match,
            Title: "Có chuyển khoản gộp mới",
            Message: $"{booking.Player?.User.Username ?? "Thành viên"} đã tải lên biên lai chuyển khoản gộp cho {payments.Count} phần trong trận ghép.",
            Tone: NotificationTones.Info,
            LinkTo: $"/owner/bookings/{booking.BookingId}",
            LinkLabel: "Xem đơn"));
        _notifications.PublishPending();

        foreach (var payment in payments)
        {
            _paymentRealtime.Publish(new PaymentChangedEvent(
                payment.PaymentId,
                payment.BookingId,
                payment.Booking.Court.VenueId,
                payment.Status,
                "BatchSubmitted"));
        }

        return Ok(new BatchPaymentResponse
        {
            BookingId = booking.BookingId,
            PaymentGroupId = newGroupId,
            SubmittedCount = payments.Count,
            PayerIds = payments.Select(item => item.PayerId).ToList(),
            Status = "WaitingForConfirmation",
            ReceiptImageUrl = receiptUrl,
            SubmittedAt = now
        });
    }

    public Task<ServiceResult<BankTransferResponse>> SubmitTransfer(int bookingId, SubmitPaymentReceiptRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<ServiceResult<BankTransferResponse>>(Ok(new BankTransferResponse()));

    public Task<ServiceResult<BatchPaymentResponse>> SubmitPlayerBookingGroupTransfer(Guid paymentGroupId, SubmitPaymentReceiptRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<ServiceResult<BatchPaymentResponse>>(Ok(new BatchPaymentResponse()));

    public Task<ServiceResult<PaginatedResponse<BankTransferResponse>>> GetOperatorPayments(string status = "WaitingForConfirmation", int page = 1, int pageSize = Pagination.DefaultPageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult<ServiceResult<PaginatedResponse<BankTransferResponse>>>(Ok(Pagination.Create(new List<BankTransferResponse>(), 0, page, pageSize)));

    public Task<ServiceResult<BankTransferResponse>> GetPlayerBookingPayment(int bookingId, CancellationToken cancellationToken) =>
        Task.FromResult<ServiceResult<BankTransferResponse>>(Ok(new BankTransferResponse()));

    public Task<ServiceResult<BankTransferResponse>> GetOperatorPayment(int paymentId, CancellationToken cancellationToken) =>
        Task.FromResult<ServiceResult<BankTransferResponse>>(Ok(new BankTransferResponse()));

    public Task<ServiceResult<List<BankTransferResponse>>> GetOperatorBookingPayments(int bookingId, CancellationToken cancellationToken) =>
        Task.FromResult<ServiceResult<List<BankTransferResponse>>>(Ok(new List<BankTransferResponse>()));

    public async Task<ServiceResult<BankTransferResponse>> ApprovePayment(int paymentId, CancellationToken cancellationToken)
    {
        var res = await ConfirmPayment(paymentId, new PaymentConfirmRequest(), cancellationToken);
        return new ServiceResult<BankTransferResponse>(res.Status, Value: new BankTransferResponse(), Error: res.Error);
    }

    public async Task<ServiceResult<BankTransferResponse>> RejectPayment(int paymentId, RejectPaymentRequest request, CancellationToken cancellationToken)
    {
        var res = await RejectPayment(paymentId, new PaymentRejectRequest { Reason = request.Reason }, cancellationToken);
        return new ServiceResult<BankTransferResponse>(res.Status, Value: new BankTransferResponse(), Error: res.Error);
    }

    public async Task<ServiceResult<PaymentDetailResponse>> GetPaymentDetail(int paymentId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var payment = await BasePaymentQuery(asTracking: false)
            .SingleOrDefaultAsync(item => item.PaymentId == paymentId, cancellationToken);
        if (payment is null) return NotFound(new { message = "Không tìm thấy khoản thanh toán." });
        if (!CanAccessPayment(payment, userId.Value)) return Forbid();

        var (bookingCode, matchCode) = BuildCodes(payment.Booking);
        return Ok(MapDetail(payment, bookingCode, matchCode));
    }

    public async Task<ServiceResult<PaymentDetailResponse>> ConfirmPayment(
        int paymentId,
        PaymentConfirmRequest request,
        CancellationToken cancellationToken)
    {
        var owner = await CurrentOwnerAsync(cancellationToken);
        if (owner is null) return Forbid();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"payment-review:{paymentId}", cancellationToken))
            return Conflict(new { message = "Thanh toán đang được xử lý. Vui lòng thử lại." });

        var target = await _paymentRepository.Payments
            .Where(item => item.PaymentId == paymentId && item.Booking.Court.Venue.OwnerId == owner.OwnerId)
            .Select(item => new { item.BookingId, item.PaymentGroupId })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null) return NotFound(new { message = "Không tìm thấy khoản thanh toán." });

        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{target.BookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var groupPayments = await _paymentRepository.Payments
            .Include(item => item.StatusHistories)
            .Include(item => item.Payer).ThenInclude(item => item.User)
            .Where(item => target.PaymentGroupId.HasValue
                ? item.PaymentGroupId == target.PaymentGroupId
                : item.PaymentId == paymentId)
            .OrderBy(item => item.PaymentId)
            .ToListAsync(cancellationToken);

        var booking = await BaseBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == target.BookingId && item.Court.Venue.OwnerId == owner.OwnerId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking thuộc khoản thanh toán." });

        if (groupPayments.Any(item => item.Status != "WaitingForConfirmation"))
            return Conflict(new { message = "Khoản thanh toán không ở trạng thái chờ duyệt." });

        var now = DateTime.UtcNow;
        var primaryPayment = groupPayments.Single(item => item.PaymentId == paymentId);
        var reference = string.IsNullOrWhiteSpace(request.TransactionReference) ? null : request.TransactionReference.Trim();

        foreach (var payment in groupPayments)
        {
            var previous = payment.Status;
            payment.Status = "Paid";
            payment.PaidAt = now;
            payment.VerifiedAt = now;
            payment.VerifiedByUserId = owner.UserId;
            payment.RejectionReason = null;

            payment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = previous,
                ToStatus = "Paid",
                Action = "OwnerConfirmed",
                Reason = reference is null ? "Chủ sân đã xác nhận thanh toán." : $"Chủ sân đã xác nhận thanh toán (Mã giao dịch: {reference}).",
                ActorUserId = owner.UserId,
                CreatedAt = now
            });
        }

        var isMatch = booking.MatchId.HasValue;
        if (!isMatch)
        {
            booking.Status = "Confirmed";
            booking.HoldExpiresAt = null;
            booking.HoldRemainingSeconds = null;
            booking.StatusHistories.Add(new BookingStatusHistory
            {
                FromStatus = "Holding",
                ToStatus = "Confirmed",
                Reason = "Chủ sân xác nhận thanh toán thành công.",
                ActorUserId = owner.UserId,
                ChangedAt = now
            });
        }
        else
        {
            var acceptedPlayerIds = booking.Match!.MatchParticipants
                .Where(IsApprovedMatchParticipant)
                .Select(item => item.PlayerId)
                .ToHashSet();
            var paidPlayerIds = booking.Payments
                .Where(item => item.Status == "Paid")
                .Select(item => item.PayerId)
                .ToHashSet();

            var allApprovedPaid = acceptedPlayerIds.Count == booking.Match.RequiredPlayerCount
                && acceptedPlayerIds.All(id => paidPlayerIds.Contains(id));

            if (allApprovedPaid)
            {
                booking.Status = "Confirmed";
                booking.Match.Status = "Booked";
                booking.HoldExpiresAt = null;
                booking.HoldRemainingSeconds = null;
                booking.StatusHistories.Add(new BookingStatusHistory
                {
                    FromStatus = "Holding",
                    ToStatus = "Confirmed",
                    Reason = "Tất cả các phần trong trận ghép đã hoàn tất thanh toán.",
                    ActorUserId = owner.UserId,
                    ChangedAt = now
                });
            }
            else
            {
                booking.Status = "Holding";
                booking.Match.Status = "BookingPending";
            }
        }

        await _paymentRepository.AddAuditLogAsync(NewAudit(booking.Court.VenueId, $"PaymentConfirmed:{paymentId}:{groupPayments.Count}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var (bookingCode, matchCode) = BuildCodes(booking);
        foreach (var payment in groupPayments)
        {
            _notifications.Add(new NotificationInput(
                UserId: payment.Payer.UserId,
                Type: isMatch ? NotificationTypes.Match : NotificationTypes.Court,
                Title: "Thanh toán đã được duyệt",
                Message: $"Chủ sân đã xác nhận thanh toán cho {(isMatch ? "phần ghép trận" : "đơn đặt sân")} {bookingCode}.",
                Tone: NotificationTones.Success,
                LinkTo: isMatch ? $"/matches/{booking.MatchId}" : "/my-bookings",
                LinkLabel: isMatch ? "Xem trận đấu" : "Xem đơn"));
        }

        _notifications.PublishPending();

        foreach (var payment in groupPayments)
        {
            _paymentRealtime.Publish(new PaymentChangedEvent(
                payment.PaymentId,
                payment.BookingId,
                booking.Court.VenueId,
                payment.Status,
                "Confirmed"));
        }

        if (isMatch)
        {
            _matchRealtime.Publish(booking.MatchId!.Value, "PaymentConfirmed");
        }

        PublishScheduleUpdate(booking, "PaymentConfirmed");
        return Ok(MapDetail(primaryPayment, bookingCode, matchCode));
    }

    public async Task<ServiceResult<PaymentDetailResponse>> RejectPayment(
        int paymentId,
        PaymentRejectRequest request,
        CancellationToken cancellationToken)
    {
        var owner = await CurrentOwnerAsync(cancellationToken);
        if (owner is null) return Forbid();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"payment-review:{paymentId}", cancellationToken))
            return Conflict(new { message = "Thanh toán đang được xử lý. Vui lòng thử lại." });

        var target = await _paymentRepository.Payments
            .Where(item => item.PaymentId == paymentId && item.Booking.Court.Venue.OwnerId == owner.OwnerId)
            .Select(item => new { item.BookingId, item.PaymentGroupId })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null) return NotFound(new { message = "Không tìm thấy khoản thanh toán." });

        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{target.BookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var groupPayments = await _paymentRepository.Payments
            .Include(item => item.StatusHistories)
            .Include(item => item.Payer).ThenInclude(item => item.User)
            .Where(item => target.PaymentGroupId.HasValue
                ? item.PaymentGroupId == target.PaymentGroupId
                : item.PaymentId == paymentId)
            .OrderBy(item => item.PaymentId)
            .ToListAsync(cancellationToken);

        var booking = await BaseBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == target.BookingId && item.Court.Venue.OwnerId == owner.OwnerId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking thuộc khoản thanh toán." });

        if (groupPayments.Any(item => item.Status != "WaitingForConfirmation"))
            return Conflict(new { message = "Khoản thanh toán không ở trạng thái chờ duyệt." });

        var now = DateTime.UtcNow;
        var reason = request.Reason.Trim();
        var primaryPayment = groupPayments.Single(item => item.PaymentId == paymentId);

        foreach (var payment in groupPayments)
        {
            var previous = payment.Status;
            payment.Status = "Pending";
            payment.VerifiedAt = now;
            payment.VerifiedByUserId = owner.UserId;
            payment.RejectionReason = reason;
            payment.PaymentGroupId = null;

            payment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = previous,
                ToStatus = "Pending",
                Action = "OwnerRejected",
                Reason = reason,
                ActorUserId = owner.UserId,
                CreatedAt = now
            });
        }

        await _paymentRepository.AddAuditLogAsync(NewAudit(booking.Court.VenueId, $"PaymentRejected:{paymentId}:{reason}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var (bookingCode, matchCode) = BuildCodes(booking);
        var isMatch = booking.MatchId.HasValue;

        foreach (var payment in groupPayments)
        {
            _notifications.Add(new NotificationInput(
                UserId: payment.Payer.UserId,
                Type: isMatch ? NotificationTypes.Match : NotificationTypes.Court,
                Title: "Thanh toán bị từ chối",
                Message: $"Biên lai thanh toán cho {bookingCode} bị từ chối. Lý do: {reason}",
                Tone: NotificationTones.Urgent,
                LinkTo: isMatch ? $"/matches/{booking.MatchId}" : "/my-bookings",
                LinkLabel: "Tải lại biên lai"));
        }

        _notifications.PublishPending();

        foreach (var payment in groupPayments)
        {
            _paymentRealtime.Publish(new PaymentChangedEvent(
                payment.PaymentId,
                payment.BookingId,
                booking.Court.VenueId,
                payment.Status,
                "Rejected"));
        }

        return Ok(MapDetail(primaryPayment, bookingCode, matchCode));
    }

    private int? CurrentUserId() => _currentUserId;

    private async Task<VenueOwner?> CurrentOwnerAsync(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return null;
        return await _paymentRepository.Venues
            .Where(v => v.Owner.UserId == userId.Value)
            .Select(v => v.Owner)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private IQueryable<Payment> BasePaymentQuery(bool asTracking)
    {
        var query = _paymentRepository.Payments;
        if (!asTracking) query = query.AsNoTracking();

        return query
            .Include(item => item.Payer).ThenInclude(item => item.User)
            .Include(item => item.StatusHistories)
            .Include(item => item.Booking).ThenInclude(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner)
            .Include(item => item.Booking).ThenInclude(item => item.Player).ThenInclude(item => item!.User)
            .Include(item => item.Booking).ThenInclude(item => item.Match);
    }

    private IQueryable<Booking> BaseBookingQuery(bool asTracking)
    {
        var query = _paymentRepository.Bookings;
        if (!asTracking) query = query.AsNoTracking();

        return query
            .Include(item => item.StatusHistories)
            .Include(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner)
            .Include(item => item.Payments).ThenInclude(item => item.StatusHistories)
            .Include(item => item.Player).ThenInclude(item => item!.User)
            .Include(item => item.Match).ThenInclude(item => item!.MatchParticipants);
    }

    private IQueryable<Booking> BatchPaymentBookingQuery(bool asTracking)
    {
        var query = _paymentRepository.Bookings;
        if (!asTracking) query = query.AsNoTracking();

        return query
            .Include(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner)
            .Include(item => item.Player).ThenInclude(item => item!.User)
            .Include(item => item.Match).ThenInclude(item => item!.MatchParticipants).ThenInclude(item => item.Player).ThenInclude(item => item.User)
            .Include(item => item.Payments).ThenInclude(item => item.Payer).ThenInclude(item => item.User);
    }

    private static bool IsApprovedMatchParticipant(MatchParticipant participant) =>
        string.Equals(participant.Status, "Approved", StringComparison.OrdinalIgnoreCase)
        || string.Equals(participant.Status, "Accepted", StringComparison.OrdinalIgnoreCase);

    private static bool RebalancePendingMatchPayments(Booking booking)
    {
        if (booking.Match is null || booking.Payments.Count == 0)
            return false;

        var approvedParticipants = booking.Match.MatchParticipants
            .Where(IsApprovedMatchParticipant)
            .OrderBy(item => item.RequestedAt)
            .ToList();
        if (approvedParticipants.Count == 0)
            return false;

        var perPlayerAmount = decimal.Round(booking.TotalAmount / approvedParticipants.Count, 0, MidpointRounding.AwayFromZero);
        var changed = false;

        foreach (var participant in approvedParticipants)
        {
            var payment = booking.Payments.FirstOrDefault(p => p.PayerId == participant.PlayerId);
            if (payment is null)
            {
                payment = new Payment
                {
                    BookingId = booking.BookingId,
                    PayerId = participant.PlayerId,
                    Amount = perPlayerAmount,
                    Status = "Pending",
                    SubmittedAt = DateTime.UtcNow
                };
                booking.Payments.Add(payment);
                changed = true;
            }
            else if (payment.Status == "Pending" && payment.Amount != perPlayerAmount)
            {
                payment.Amount = perPlayerAmount;
                changed = true;
            }
        }

        return changed;
    }

    private static bool HasOneConfiguredBankAccount(IReadOnlyList<Payment> payments)
    {
        if (payments.Count == 0) return false;
        var first = payments[0];
        if (string.IsNullOrWhiteSpace(first.BankCode) || string.IsNullOrWhiteSpace(first.BankAccountNumber) || string.IsNullOrWhiteSpace(first.BankAccountName))
            return false;

        return payments.All(item =>
            string.Equals(item.BankCode, first.BankCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.BankAccountNumber, first.BankAccountNumber, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.BankAccountName, first.BankAccountName, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildBatchTransferContent(Booking booking, HashSet<int> payerIds)
    {
        var seed = $"{booking.BookingId}:{string.Join("-", payerIds.OrderBy(id => id))}:{booking.CreatedAt:yyyyMMddHHmmss}";
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(seed));
        var hex = Convert.ToHexString(hash)[..16].ToUpperInvariant();
        return $"PLG-{hex}";
    }

    private static string BuildBatchVietQrUrl(
        string bankCode,
        string accountNumber,
        string accountName,
        decimal amount,
        string content)
    {
        var encodedName = Uri.EscapeDataString(accountName);
        var encodedContent = Uri.EscapeDataString(content);
        return $"https://img.vietqr.io/image/{bankCode}-{accountNumber}-compact2.png?amount={(long)amount}&addInfo={encodedContent}&accountName={encodedName}";
    }

    private async Task<string> SaveReceiptAsync(int bookingId, IFormFile receipt, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(receipt.FileName).ToLowerInvariant();
        if (!AllowedReceiptTypes.Contains(receipt.ContentType)) extension = ".jpg";

        var fileName = $"receipt-{bookingId}-{Guid.NewGuid():N}{extension}";
        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var directory = Path.Combine(webRoot, "uploads", "receipts");
        Directory.CreateDirectory(directory);

        var fullPath = Path.Combine(directory, fileName);
        await using (var stream = System.IO.File.Create(fullPath))
        {
            await receipt.CopyToAsync(stream, cancellationToken);
        }

        var publicBaseUrl = _configuration["PublicBaseUrl"]?.TrimEnd('/');
        var relativeUrl = $"/uploads/receipts/{fileName}";
        return string.IsNullOrWhiteSpace(publicBaseUrl) ? relativeUrl : $"{publicBaseUrl}{relativeUrl}";
    }

    private static bool CanAccessPayment(Payment payment, int userId) =>
        payment.Payer.UserId == userId || payment.Booking.Court.Venue.Owner.UserId == userId;

    private static (string BookingCode, string? MatchCode) BuildCodes(Booking booking) =>
        (booking.BookingCode ?? $"PL-{booking.BookingId}", booking.MatchId.HasValue ? $"MATCH-{booking.MatchId}" : null);

    private static VenueAuditLog NewAudit(int venueId, string action) => new()
    {
        VenueId = venueId,
        ActorId = 0,
        Action = action,
        Timestamp = DateTime.UtcNow
    };

    private static OwnerBankAccountResponse MapAccount(OwnerBankAccount account) => new()
    {
        BankName = account.BankName,
        AccountNo = account.AccountNumber,
        AccountHolderName = account.AccountHolderName
    };

    private static PaymentDetailResponse MapDetail(Payment payment, string bookingCode, string? matchCode) => new()
    {
        PaymentId = payment.PaymentId,
        BookingId = payment.BookingId,
        BookingCode = bookingCode,
        MatchCode = matchCode,
        PayerName = payment.Payer.User.Username,
        Amount = payment.Amount,
        PaymentMethod = payment.PaymentMethod ?? "BankTransfer",
        Status = payment.Status,
        TransferCode = payment.TransferCode,
        BankName = payment.BankName,
        BankCode = payment.BankCode,
        BankAccountNumber = payment.BankAccountNumber,
        BankAccountName = payment.BankAccountName,
        ReceiptImageUrl = payment.ReceiptImageUrl,
        RejectionReason = payment.RejectionReason,
        SubmittedAt = payment.SubmittedAt,
        VerifiedAt = payment.VerifiedAt,
        History = payment.StatusHistories
            .OrderBy(h => h.CreatedAt)
            .Select(h => new PaymentHistoryResponse
            {
                FromStatus = h.FromStatus,
                ToStatus = h.ToStatus,
                Action = h.Action,
                Reason = h.Reason,
                CreatedAt = h.CreatedAt
            })
            .ToList()
    };

    private void PublishScheduleUpdate(Booking booking, string action)
    {
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            booking.Court.VenueId,
            booking.CourtId,
            booking.StartTime,
            booking.EndTime,
            booking.Status,
            action));
    }
}
