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
using PicklinkBackend.Services.Security;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Payments.Implementations;

public sealed record PaymentServiceDependencies(
    IPaymentRepository PaymentRepository,
    IWebHostEnvironment Environment,
    IConfiguration Configuration,
    ScheduleRealtimeNotifier ScheduleRealtime,
    PaymentRealtimeNotifier PaymentRealtime,
    MatchRealtimeNotifier MatchRealtime,
    NotificationService Notifications,
    SePayReconciliationService SePayReconciliation,
    IEncryptionService EncryptionService,
    CloudinaryUploadService CloudinaryUpload);

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
    private readonly SePayReconciliationService _sePayReconciliation;
    private readonly IEncryptionService _encryptionService;
    private readonly CloudinaryUploadService _cloudinaryUpload;
    private int? _currentUserId;

    private PaymentService(
        IPaymentRepository paymentRepository,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ScheduleRealtimeNotifier scheduleRealtime,
        PaymentRealtimeNotifier paymentRealtime,
        MatchRealtimeNotifier matchRealtime,
        NotificationService notifications,
        SePayReconciliationService sePayReconciliation,
        IEncryptionService encryptionService,
        CloudinaryUploadService cloudinaryUpload)
    {
        _paymentRepository = paymentRepository;
        _environment = environment;
        _configuration = configuration;
        _scheduleRealtime = scheduleRealtime;
        _paymentRealtime = paymentRealtime;
        _matchRealtime = matchRealtime;
        _notifications = notifications;
        _sePayReconciliation = sePayReconciliation;
        _encryptionService = encryptionService;
        _cloudinaryUpload = cloudinaryUpload;
    }

    public PaymentService(PaymentServiceDependencies dependencies)
        : this(
            dependencies.PaymentRepository,
            dependencies.Environment,
            dependencies.Configuration,
            dependencies.ScheduleRealtime,
            dependencies.PaymentRealtime,
            dependencies.MatchRealtime,
            dependencies.Notifications,
            dependencies.SePayReconciliation,
            dependencies.EncryptionService,
            dependencies.CloudinaryUpload)
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
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
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
        // A null token means "leave what is stored alone"; an empty string clears it. Anything
        // else is a new token and only ever reaches the database encrypted.
        if (request.SePayApiToken is not null)
        {
            var token = request.SePayApiToken.Trim();
            account.SePayApiToken = token.Length == 0 ? null : _encryptionService.Encrypt(token);
        }
        account.IsActive = true;
        account.UpdatedAt = DateTime.UtcNow;
        foreach (var venueId in await _paymentRepository.Venues.Where(item => item.OwnerId == owner.OwnerId).Select(item => item.VenueId).ToListAsync(cancellationToken))
            await _paymentRepository.AddAuditLogAsync(NewAudit(venueId, userId.Value, "BankAccountUpdated"), cancellationToken);
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

        await using var transaction = await _paymentRepository.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction, $"booking-payment:{bookingId}", cancellationToken))
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
        await EnsurePaymentsHaveConfiguredBankAccountAsync(booking, payments, cancellationToken);
        if (!HasOneConfiguredBankAccount(payments))
            return Conflict(new { message = "Chủ sân chưa cấu hình tài khoản ngân hàng nhận tiền. Vui lòng liên hệ chủ sân để cập nhật tài khoản thanh toán." });

        var transferContent = BuildBatchTransferContent(booking, targetParticipantIds);
        var totalAmount = payments.Sum(item => item.Amount);
        var paymentGroupId = payments.Count > 1 ? Guid.NewGuid() : (Guid?)null;
        var qrImageUrl = BuildBatchVietQrUrl(
            payments[0].BankCode!,
            payments[0].BankAccountNumber!,
            payments[0].BankAccountName!,
            totalAmount,
            transferContent);
        foreach (var payment in payments)
        {
            payment.PaymentGroupId = paymentGroupId;
            payment.TransferContent = transferContent;
            payment.QrImageUrl = qrImageUrl;
        }
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(new BatchPaymentPreviewResponse
        {
            BookingId = booking.BookingId,
            PayerIds = payments.Select(item => item.PayerId).ToList(),
            MemberNames = payments.Select(item => item.Payer.User.Username).ToList(),
            TotalAmount = totalAmount,
            TransferContent = transferContent,
            QrImageUrl = qrImageUrl
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
        await EnsurePaymentsHaveConfiguredBankAccountAsync(booking, payments, cancellationToken);
        if (!HasOneConfiguredBankAccount(payments))
            return Conflict(new { message = "Chủ sân chưa cấu hình tài khoản ngân hàng nhận tiền. Vui lòng liên hệ chủ sân để cập nhật tài khoản thanh toán." });

        var now = DateTime.UtcNow;
        var transferContent = BuildBatchTransferContent(booking, targetParticipantIds);
        string receiptUrl;
        try
        {
            receiptUrl = await SaveReceiptAsync(booking.BookingId, receipt, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        var newGroupId = Guid.NewGuid();

        foreach (var payment in payments)
        {
            var previous = payment.Status;
            payment.Status = "WaitingForConfirmation";
            payment.SubmittedAt = now;
            payment.TransferCode = string.IsNullOrWhiteSpace(payment.TransferCode)
                ? $"PL{DateTime.UtcNow:yyyyMMdd}{Guid.NewGuid():N}"[..20].ToUpperInvariant()
                : payment.TransferCode;
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

        PauseBookingHold(booking, now);
        await _paymentRepository.AddAuditLogAsync(NewAudit(booking.Court.VenueId, userId.Value, $"BatchPaymentSubmitted:{booking.BookingId}:{payments.Count}"), cancellationToken);
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

    public async Task<ServiceResult<BankTransferResponse>> SubmitTransfer(
        int bookingId,
        SubmitPaymentReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var receipt = request.Receipt;
        if (receipt is null || receipt.Length == 0)
            return BadRequest(new { message = "Vui lòng tải ảnh biên lai." });
        if (receipt.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "Ảnh biên lai không được vượt quá 5 MB." });
        if (!AllowedReceiptTypes.Contains(receipt.ContentType))
            return BadRequest(new { message = "Biên lai chỉ hỗ trợ JPG, PNG hoặc WEBP." });
        if (!await ImageUploadPolicy.HasValidSignatureAsync(receipt, cancellationToken))
            return BadRequest(new { message = "Nội dung tệp biên lai không khớp với định dạng ảnh." });

        var playerId = await _paymentRepository.Players
            .Where(item => item.UserId == userId.Value)
            .Select(item => (int?)item.PlayerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (playerId is null) return Forbid();
        if (request.PayerId.HasValue && request.PayerId != playerId.Value) return Forbid();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await BaseBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && !item.MatchId.HasValue, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking cần thanh toán." });
        if (booking.PlayerId != playerId.Value) return Forbid();
        if (booking.Status != "Holding" || !booking.HoldExpiresAt.HasValue || booking.HoldExpiresAt <= DateTime.UtcNow)
            return Conflict(new { message = "Booking không còn trong thời gian giữ chỗ." });

        var payment = booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault();
        if (payment is null) return NotFound(new { message = "Không tìm thấy khoản thanh toán của booking." });
        if (payment.PayerId != playerId.Value) return Forbid();
        if (payment.Status != "Pending")
            return Conflict(new { message = "Khoản thanh toán không còn ở trạng thái chờ gửi biên lai." });
        if (string.IsNullOrWhiteSpace(payment.BankCode)
            || string.IsNullOrWhiteSpace(payment.BankAccountNumber)
            || string.IsNullOrWhiteSpace(payment.BankAccountName))
            return Conflict(new { message = "Sân chưa cấu hình tài khoản nhận tiền hợp lệ." });

        var now = DateTime.UtcNow;
        payment.Status = "WaitingForConfirmation";
        payment.PaymentMethod = "BankTransfer";
        payment.SubmittedAt = now;
        try
        {
            payment.ReceiptImageUrl = await SaveReceiptAsync(booking.BookingId, receipt, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        payment.RejectionReason = null;
        payment.StatusHistories.Add(new PaymentStatusHistory
        {
            FromStatus = "Pending",
            ToStatus = "WaitingForConfirmation",
            Action = "ReceiptSubmitted",
            Reason = "Người chơi đã gửi biên lai chuyển khoản.",
            ActorUserId = userId.Value,
            CreatedAt = now
        });

        PauseBookingHold(booking, now);

        await _paymentRepository.AddAuditLogAsync(NewAudit(
            booking.Court.VenueId,
            userId.Value,
            $"PaymentReceiptSubmitted:{payment.PaymentId}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _notifications.Add(new NotificationInput(
            UserId: booking.Court.Venue.Owner.UserId,
            Type: NotificationTypes.Court,
            Title: "Có biên lai chuyển khoản mới",
            Message: $"{booking.Player?.User.Username ?? "Người chơi"} đã gửi biên lai cho đơn {booking.BookingCode ?? $"PL-{booking.BookingId}"}.",
            Tone: NotificationTones.Info,
            LinkTo: $"/owner/bookings/{booking.BookingId}",
            LinkLabel: "Xem đơn"));
        _notifications.PublishPending();
        _paymentRealtime.Publish(new PaymentChangedEvent(
            payment.PaymentId,
            booking.BookingId,
            booking.Court.VenueId,
            payment.Status,
            "ReceiptSubmitted"));

        return Ok(MapSubmittedTransfer(payment, booking));
    }

    public async Task<ServiceResult<BankTransferResponse>> SubmitTicketTransfer(
        int sessionTicketId,
        SubmitPaymentReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var receipt = request.Receipt;
        if (receipt is null || receipt.Length == 0)
            return BadRequest(new { message = "Vui lòng tải ảnh biên lai." });
        if (receipt.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "Ảnh biên lai không được vượt quá 5 MB." });
        if (!AllowedReceiptTypes.Contains(receipt.ContentType))
            return BadRequest(new { message = "Biên lai chỉ hỗ trợ JPG, PNG hoặc WEBP." });
        if (!await ImageUploadPolicy.HasValidSignatureAsync(receipt, cancellationToken))
            return BadRequest(new { message = "Nội dung tệp biên lai không khớp với định dạng ảnh." });

        var identity = await _paymentRepository.SessionTickets.AsNoTracking()
            .Where(item => item.SessionTicketId == sessionTicketId && item.Player.UserId == userId.Value)
            .Select(item => new { item.TicketSessionId, item.TicketSession.BookingId })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity is null) return NotFound(new { message = "Không tìm thấy vé cần thanh toán." });

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"ticket-session:{identity.TicketSessionId}", cancellationToken))
            return Conflict(new { message = "Vé đang được xử lý. Vui lòng thử lại." });

        var ticket = await _paymentRepository.SessionTickets
            .Include(item => item.Payment).ThenInclude(item => item.StatusHistories)
            .Include(item => item.Player).ThenInclude(item => item.User)
            .Include(item => item.TicketSession).ThenInclude(item => item.Booking)
                .ThenInclude(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner).ThenInclude(item => item.User)
            .Include(item => item.TicketSession).ThenInclude(item => item.Booking)
                .ThenInclude(item => item.Slots).ThenInclude(item => item.Court)
            .SingleAsync(item => item.SessionTicketId == sessionTicketId, cancellationToken);
        if (ticket.Status != "PendingPayment" || ticket.Payment.Status != "Pending"
            || !ticket.HoldExpiresAt.HasValue || ticket.HoldExpiresAt <= DateTime.UtcNow)
            return Conflict(new { message = "Vé không còn trong thời gian gửi biên lai." });

        var now = DateTime.UtcNow;
        ticket.Payment.Status = "WaitingForConfirmation";
        ticket.Payment.PaymentMethod = "BankTransfer";
        ticket.Payment.SubmittedAt = now;
        try
        {
            ticket.Payment.ReceiptImageUrl = await SaveReceiptAsync(identity.BookingId, receipt, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        ticket.Payment.RejectionReason = null;
        ticket.Payment.StatusHistories.Add(new PaymentStatusHistory
        {
            FromStatus = "Pending",
            ToStatus = "WaitingForConfirmation",
            Action = "ReceiptSubmitted",
            Reason = "Người chơi đã gửi biên lai mua vé.",
            ActorUserId = userId.Value,
            CreatedAt = now
        });
        ticket.HoldExpiresAt = null;

        await _paymentRepository.AddAuditLogAsync(NewAudit(
            ticket.TicketSession.Booking.Court.VenueId, userId.Value,
            $"TicketReceiptSubmitted:{ticket.PaymentId}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _notifications.Add(new NotificationInput(
            ticket.TicketSession.Booking.Court.Venue.Owner.UserId,
            NotificationTypes.Ticket,
            "Có biên lai mua vé mới",
            $"{ticket.Player.User.Username} đã gửi biên lai cho vé {ticket.TicketCode}.",
            NotificationTones.Info,
            $"/owner/ticket-sessions/{ticket.TicketSessionId}",
            "Kiểm tra biên lai"));
        _notifications.PublishPending();
        _paymentRealtime.Publish(new PaymentChangedEvent(
            ticket.PaymentId, identity.BookingId, ticket.TicketSession.Booking.Court.VenueId,
            ticket.Payment.Status, "ReceiptSubmitted"));

        return Ok(MapSubmittedTransfer(ticket.Payment, ticket.TicketSession.Booking));
    }

    public Task<ServiceResult<BatchPaymentResponse>> SubmitPlayerBookingGroupTransfer(Guid paymentGroupId, SubmitPaymentReceiptRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<ServiceResult<BatchPaymentResponse>>(Ok(new BatchPaymentResponse()));

    public Task<ServiceResult<PaginatedResponse<BankTransferResponse>>> GetOperatorPayments(string status = "WaitingForConfirmation", int page = 1, int pageSize = Pagination.DefaultPageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult<ServiceResult<PaginatedResponse<BankTransferResponse>>>(Ok(Pagination.Create(new List<BankTransferResponse>(), 0, page, pageSize)));

    public async Task<ServiceResult<BankTransferResponse>> GetPlayerBookingPayment(
        int bookingId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var payment = await BasePaymentQuery(asTracking: false)
            .Where(item => item.BookingId == bookingId
                && item.Booking.Player != null
                && item.Booking.Player.UserId == userId.Value)
            .OrderByDescending(item => item.PaymentId)
            .FirstOrDefaultAsync(cancellationToken);
        if (payment is null) return NotFound(new { message = "Không tìm thấy khoản thanh toán của booking." });

        // Checkout screens poll this endpoint while waiting for payment, so piggyback a
        // throttled SePay lookup here instead of waiting solely on the inbound webhook.
        if (payment.Status is "Pending" or "WaitingForConfirmation"
            && !string.IsNullOrWhiteSpace(payment.TransferContent)
            && await _sePayReconciliation.TryReconcileAsync(payment.TransferContent, cancellationToken))
        {
            payment = await BasePaymentQuery(asTracking: false)
                .SingleAsync(item => item.PaymentId == payment.PaymentId, cancellationToken);
        }

        return Ok(MapSubmittedTransfer(payment, payment.Booking));
    }

    public async Task<ServiceResult<BankTransferResponse>> GetOperatorPayment(int paymentId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var payment = await BasePaymentQuery(asTracking: false)
            .SingleOrDefaultAsync(item => item.PaymentId == paymentId, cancellationToken);
        if (payment is null) return NotFound(new { message = "Không tìm thấy khoản thanh toán." });

        if (payment.Booking.Court.Venue.Owner.UserId != userId.Value)
            return Forbid();

        return Ok(MapSubmittedTransfer(payment, payment.Booking));
    }

    public async Task<ServiceResult<List<BankTransferResponse>>> GetOperatorBookingPayments(
        int bookingId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var payments = await BasePaymentQuery(asTracking: false)
            .Where(item => item.BookingId == bookingId
                && item.Booking.Court.Venue.Owner.UserId == userId.Value)
            .OrderBy(item => item.PaymentId)
            .ToListAsync(cancellationToken);
        if (payments.Count == 0)
            return NotFound(new { message = "Không tìm thấy thanh toán của booking." });

        return Ok(payments.Select(payment => MapSubmittedTransfer(payment, payment.Booking)).ToList());
    }

    public async Task<ServiceResult<BankTransferResponse>> ApprovePayment(int paymentId, CancellationToken cancellationToken)
    {
        var res = await ConfirmPayment(paymentId, new PaymentConfirmRequest(), cancellationToken);
        return new ServiceResult<BankTransferResponse>(res.Status, Value: res.Value, Error: res.Error);
    }

    public async Task<ServiceResult<BankTransferResponse>> RejectPayment(int paymentId, RejectPaymentRequest request, CancellationToken cancellationToken)
    {
        var res = await RejectPayment(paymentId, new PaymentRejectRequest { Reason = request.Reason }, cancellationToken);
        return new ServiceResult<BankTransferResponse>(res.Status, Value: res.Value, Error: res.Error);
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
        return Ok(MapDetail(payment, payment.Booking, bookingCode, matchCode));
    }

    public async Task<ServiceResult<PaymentDetailResponse>> ConfirmPayment(
        int paymentId,
        PaymentConfirmRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var target = await _paymentRepository.Payments
            .Where(item => item.PaymentId == paymentId && item.Booking.Court.Venue.Owner.UserId == userId.Value)
            .Select(item => new { item.BookingId, item.PaymentGroupId, item.Booking.Court.Venue.OwnerId })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null) return NotFound(new { message = "Không tìm thấy khoản thanh toán." });

        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{target.BookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await BaseBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == target.BookingId && item.Court.Venue.OwnerId == target.OwnerId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking thuộc khoản thanh toán." });

        var groupPayments = booking.Payments
            .Where(item => target.PaymentGroupId.HasValue
                ? item.PaymentGroupId == target.PaymentGroupId
                : item.PaymentId == paymentId)
            .OrderBy(item => item.PaymentId)
            .ToList();
        var paymentIds = groupPayments.Select(item => item.PaymentId).ToList();
        var ticketPayments = booking.OwnerEntryType == "TicketSession"
            ? await _paymentRepository.SessionTickets
                .Where(item => paymentIds.Contains(item.PaymentId))
                .ToListAsync(cancellationToken)
            : [];

        if (groupPayments.Count == 0 || groupPayments.Any(item => item.Status != "WaitingForConfirmation"))
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
            payment.VerifiedByUserId = userId.Value;
            payment.RejectionReason = null;

            payment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = previous,
                ToStatus = "Paid",
                Action = "OwnerConfirmed",
                Reason = reference is null ? "Chủ sân đã xác nhận thanh toán." : $"Chủ sân đã xác nhận thanh toán (Mã giao dịch: {reference}).",
                ActorUserId = userId.Value,
                CreatedAt = now
            });
        }

        var isMatch = booking.MatchId.HasValue;
        var isTicketSession = ticketPayments.Count > 0;
        if (isTicketSession)
        {
            foreach (var ticket in ticketPayments)
            {
                ticket.Status = "Paid";
                ticket.HoldExpiresAt = null;
            }
        }
        else if (!isMatch)
        {
            booking.Status = "Confirmed";
            booking.HoldExpiresAt = null;
            booking.HoldRemainingSeconds = null;
            booking.StatusHistories.Add(new BookingStatusHistory
            {
                FromStatus = "Holding",
                ToStatus = "Confirmed",
                Reason = "Chủ sân xác nhận thanh toán thành công.",
                ActorUserId = userId.Value,
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
                    ActorUserId = userId.Value,
                    ChangedAt = now
                });
            }
            else
            {
                booking.Status = "Holding";
                booking.Match.Status = "BookingPending";
                ResumeBookingHoldIfNoPendingReview(booking, now);
            }
        }

        await _paymentRepository.AddAuditLogAsync(NewAudit(booking.Court.VenueId, userId.Value, $"PaymentConfirmed:{paymentId}:{groupPayments.Count}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var (bookingCode, matchCode) = BuildCodes(booking);
        foreach (var payment in groupPayments)
        {
            var ticket = ticketPayments.SingleOrDefault(item => item.PaymentId == payment.PaymentId);
            _notifications.Add(new NotificationInput(
                UserId: payment.Payer.UserId,
                Type: ticket is not null ? NotificationTypes.Ticket : isMatch ? NotificationTypes.Match : NotificationTypes.Court,
                Title: "Thanh toán đã được duyệt",
                Message: ticket is not null
                    ? $"Chủ sân đã xác nhận thanh toán cho vé {ticket.TicketCode}."
                    : $"Chủ sân đã xác nhận thanh toán cho {(isMatch ? "phần ghép trận" : "đơn đặt sân")} {bookingCode}.",
                Tone: NotificationTones.Success,
                LinkTo: ticket is not null ? $"/my-tickets/{ticket.SessionTicketId}" : isMatch ? $"/matches/{booking.MatchId}" : "/my-bookings",
                LinkLabel: ticket is not null ? "Xem vé" : isMatch ? "Xem trận đấu" : "Xem đơn"));
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
        return Ok(MapDetail(primaryPayment, booking, bookingCode, matchCode));
    }

    public async Task<ServiceResult<PaymentDetailResponse>> RejectPayment(
        int paymentId,
        PaymentRejectRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var target = await _paymentRepository.Payments
            .Where(item => item.PaymentId == paymentId && item.Booking.Court.Venue.Owner.UserId == userId.Value)
            .Select(item => new { item.BookingId, item.PaymentGroupId, item.Booking.Court.Venue.OwnerId })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null) return NotFound(new { message = "Không tìm thấy khoản thanh toán." });

        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{target.BookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await BaseBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == target.BookingId && item.Court.Venue.OwnerId == target.OwnerId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking thuộc khoản thanh toán." });

        var groupPayments = booking.Payments
            .Where(item => target.PaymentGroupId.HasValue
                ? item.PaymentGroupId == target.PaymentGroupId
                : item.PaymentId == paymentId)
            .OrderBy(item => item.PaymentId)
            .ToList();
        var paymentIds = groupPayments.Select(item => item.PaymentId).ToList();
        var ticketPayments = booking.OwnerEntryType == "TicketSession"
            ? await _paymentRepository.SessionTickets
                .Where(item => paymentIds.Contains(item.PaymentId))
                .ToListAsync(cancellationToken)
            : [];

        if (groupPayments.Count == 0 || groupPayments.Any(item => item.Status != "WaitingForConfirmation"))
            return Conflict(new { message = "Khoản thanh toán không ở trạng thái chờ duyệt." });

        var now = DateTime.UtcNow;
        var reason = request.Reason.Trim();
        var primaryPayment = groupPayments.Single(item => item.PaymentId == paymentId);

        foreach (var payment in groupPayments)
        {
            var previous = payment.Status;
            payment.Status = "Pending";
            payment.VerifiedAt = now;
            payment.VerifiedByUserId = userId.Value;
            payment.RejectionReason = reason;
            payment.PaymentGroupId = null;

            payment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = previous,
                ToStatus = "Pending",
                Action = "OwnerRejected",
                Reason = reason,
                ActorUserId = userId.Value,
                CreatedAt = now
            });
        }

        if (ticketPayments.Count > 0)
        {
            var holdMinutes = Math.Clamp(_configuration.GetValue("Ticketing:PaymentHoldMinutes", 5), 1, 60);
            foreach (var ticket in ticketPayments)
                ticket.HoldExpiresAt = now.AddMinutes(holdMinutes);
        }
        else
        {
            ResumeBookingHoldIfNoPendingReview(booking, now);
        }

        await _paymentRepository.AddAuditLogAsync(NewAudit(booking.Court.VenueId, userId.Value, $"PaymentRejected:{paymentId}:{reason}"), cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var (bookingCode, matchCode) = BuildCodes(booking);
        var isMatch = booking.MatchId.HasValue;

        foreach (var payment in groupPayments)
        {
            var ticket = ticketPayments.SingleOrDefault(item => item.PaymentId == payment.PaymentId);
            _notifications.Add(new NotificationInput(
                UserId: payment.Payer.UserId,
                Type: ticket is not null ? NotificationTypes.Ticket : isMatch ? NotificationTypes.Match : NotificationTypes.Court,
                Title: "Thanh toán bị từ chối",
                Message: $"Biên lai thanh toán cho {(ticket is null ? bookingCode : $"vé {ticket.TicketCode}")} bị từ chối. Lý do: {reason}",
                Tone: NotificationTones.Urgent,
                LinkTo: ticket is not null ? $"/my-tickets/{ticket.SessionTicketId}" : isMatch ? $"/matches/{booking.MatchId}" : "/my-bookings",
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

        return Ok(MapDetail(primaryPayment, booking, bookingCode, matchCode));
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
        if (!asTracking) query = query.AsNoTrackingWithIdentityResolution().AsSplitQuery();

        return query
            .Include(item => item.Payer).ThenInclude(item => item.User)
            .Include(item => item.StatusHistories)
            .Include(item => item.Booking).ThenInclude(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner)
            .Include(item => item.Booking).ThenInclude(item => item.Player).ThenInclude(item => item!.User)
            .Include(item => item.Booking).ThenInclude(item => item.Payments)
            .Include(item => item.Booking).ThenInclude(item => item.Slots).ThenInclude(item => item.Court)
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
            .Include(item => item.Payments).ThenInclude(item => item.Payer).ThenInclude(item => item.User)
            .Include(item => item.Slots).ThenInclude(item => item.Court)
            .Include(item => item.Player).ThenInclude(item => item!.User)
            .Include(item => item.Match).ThenInclude(item => item!.MatchParticipants);
    }

    private IQueryable<Booking> BatchPaymentBookingQuery(bool asTracking)
    {
        var query = _paymentRepository.Bookings;
        if (!asTracking) query = query.AsNoTracking();

        return query
            .Include(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner)
            .Include(item => item.Slots).ThenInclude(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner)
            .Include(item => item.Player).ThenInclude(item => item!.User)
            .Include(item => item.Match).ThenInclude(item => item!.MatchParticipants).ThenInclude(item => item.Player).ThenInclude(item => item.User)
            .Include(item => item.Payments).ThenInclude(item => item.Payer).ThenInclude(item => item.User);
    }

    private async Task EnsurePaymentsHaveConfiguredBankAccountAsync(
        Booking booking,
        IReadOnlyList<Payment> payments,
        CancellationToken cancellationToken)
    {
        if (payments.Count == 0 || !payments.Any(p => string.IsNullOrWhiteSpace(p.BankAccountNumber)))
            return;

        var ownerId = booking.Court?.Venue?.OwnerId
            ?? booking.Slots.FirstOrDefault()?.Court?.Venue?.OwnerId;

        if (ownerId.HasValue)
        {
            var activeBank = await _paymentRepository.OwnerBankAccounts
                .FirstOrDefaultAsync(b => b.OwnerId == ownerId.Value && b.IsActive, cancellationToken);
            if (activeBank is not null)
            {
                foreach (var payment in payments.Where(p => string.IsNullOrWhiteSpace(p.BankAccountNumber)))
                {
                    payment.BankCode = activeBank.BankCode;
                    payment.BankName = activeBank.BankName;
                    payment.BankAccountNumber = activeBank.AccountNumber;
                    payment.BankAccountName = activeBank.AccountHolderName;
                }
            }
        }
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

        var samplePayment = booking.Payments.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.BankCode));
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
                    SubmittedAt = DateTime.UtcNow,
                    BankCode = samplePayment?.BankCode,
                    BankName = samplePayment?.BankName,
                    BankAccountNumber = samplePayment?.BankAccountNumber,
                    BankAccountName = samplePayment?.BankAccountName,
                    TransferCode = $"PL{DateTime.UtcNow:yyyyMMdd}{Guid.NewGuid():N}"[..20].ToUpperInvariant()
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
        await using var stream = receipt.OpenReadStream();
        return await _cloudinaryUpload.UploadImageAsync(
            stream,
            fileName,
            "picklink_receipts",
            cancellationToken);
    }

    private static bool CanAccessPayment(Payment payment, int userId) =>
        payment.Payer.UserId == userId || payment.Booking.Court.Venue.Owner.UserId == userId;

    private static (string BookingCode, string? MatchCode) BuildCodes(Booking booking) =>
        (booking.BookingCode ?? $"PL-{booking.BookingId}", booking.MatchId.HasValue ? $"MATCH-{booking.MatchId}" : null);

    private static void PauseBookingHold(Booking booking, DateTime now)
    {
        if (!booking.HoldExpiresAt.HasValue) return;

        booking.HoldRemainingSeconds = Math.Max(
            1,
            (int)Math.Ceiling((booking.HoldExpiresAt.Value - now).TotalSeconds));
        booking.HoldExpiresAt = null;
    }

    private void ResumeBookingHoldIfNoPendingReview(Booking booking, DateTime now)
    {
        if (booking.Status != "Holding"
            || booking.Payments.Any(payment => payment.Status == "WaitingForConfirmation"))
            return;

        var configuredSeconds = Math.Clamp(_configuration.GetValue("Booking:HoldingMinutes", 5), 1, 60) * 60;
        var remainingSeconds = Math.Clamp(
            booking.HoldRemainingSeconds ?? configuredSeconds,
            1,
            configuredSeconds);
        booking.HoldExpiresAt = now.AddSeconds(remainingSeconds);
        booking.HoldRemainingSeconds = null;
    }
    private static VenueAuditLog NewAudit(int venueId, int actorId, string action) => new()
    {
        VenueId = venueId,
        ActorId = actorId,
        Action = action,
        Timestamp = DateTime.UtcNow
    };

    private OwnerBankAccountResponse MapAccount(OwnerBankAccount account) => new()
    {
        OwnerBankAccountId = account.OwnerBankAccountId,
        BankCode = account.BankCode,
        BankName = account.BankName,
        AccountNumber = account.AccountNumber,
        AccountHolderName = account.AccountHolderName,
        HasSePayApiToken = !string.IsNullOrEmpty(account.SePayApiToken),
        MaskedSePayApiToken = MaskStoredToken(account.SePayApiToken),
        IsActive = account.IsActive
    };

    /// <summary>
    /// Decrypts only far enough to build the preview. A row written under a rotated or lost key
    /// still reports as configured, just without a readable prefix -- the owner can overwrite it.
    /// </summary>
    private string? MaskStoredToken(string? encryptedToken)
    {
        if (string.IsNullOrEmpty(encryptedToken)) return null;
        try
        {
            return SecretMask.Mask(_encryptionService.Decrypt(encryptedToken));
        }
        catch (CryptographicException)
        {
            return "****";
        }
    }

    private static PaymentDetailResponse MapDetail(
        Payment payment,
        Booking booking,
        string bookingCode,
        string? matchCode) =>
        MapTransfer<PaymentDetailResponse>(payment, booking, matchCode ?? bookingCode);

    private static BankTransferResponse MapSubmittedTransfer(Payment payment, Booking booking) =>
        MapTransfer<BankTransferResponse>(payment, booking, booking.BookingCode ?? $"PL-{booking.BookingId}");

    private static TResponse MapTransfer<TResponse>(Payment payment, Booking booking, string displayCode)
        where TResponse : BankTransferResponse, new()
    {
        var groupedPayments = payment.PaymentGroupId.HasValue
            ? booking.Payments.Where(item => item.PaymentGroupId == payment.PaymentGroupId).ToList()
            : new List<Payment> { payment };
        if (groupedPayments.Count == 0) groupedPayments.Add(payment);

        return new TResponse
        {
        PaymentId = payment.PaymentId,
        PaymentGroupId = payment.PaymentGroupId,
        GroupPaymentCount = groupedPayments.Count,
        GroupTotalAmount = groupedPayments.Sum(item => item.Amount),
        BookingId = booking.BookingId,
        BookingCode = displayCode,
        BookingStatus = booking.Status,
        PaymentStatus = payment.Status,
        PaymentMethod = payment.PaymentMethod ?? "BankTransfer",
        Amount = payment.Amount,
        TransferCode = payment.TransferCode,
        TransferContent = payment.TransferContent,
        BankCode = payment.BankCode,
        BankName = payment.BankName,
        BankAccountNumber = payment.BankAccountNumber,
        BankAccountName = payment.BankAccountName,
        QrImageUrl = payment.QrImageUrl,
        ReceiptImageUrl = payment.ReceiptImageUrl,
        SubmittedAt = payment.SubmittedAt,
        VerifiedAt = payment.VerifiedAt,
        RejectionReason = payment.RejectionReason,
        HoldExpiresAt = booking.HoldExpiresAt,
        VenueId = booking.Court.VenueId,
        VenueName = booking.Court.Venue.VenueName,
        CourtNumber = booking.Court.CourtNumber,
        StartTime = booking.StartTime,
        EndTime = booking.EndTime,
        PlayerName = payment.Payer?.User.Username ?? booking.Player?.User.Username ?? string.Empty,
        Slots = booking.Slots
            .OrderBy(item => item.StartTime)
            .ThenBy(item => item.CourtId)
            .Select(item => new PaymentBookingSlotResponse
            {
                CourtId = item.CourtId,
                CourtNumber = item.Court.CourtNumber,
                StartTime = item.StartTime,
                EndTime = item.EndTime
            })
            .ToList(),
        History = payment.StatusHistories
            .OrderBy(item => item.CreatedAt)
            .Select(item => new PaymentHistoryResponse
            {
                FromStatus = item.FromStatus,
                ToStatus = item.ToStatus,
                Action = item.Action,
                Reason = item.Reason,
                CreatedAt = item.CreatedAt
            })
            .ToList()
        };
    }

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
