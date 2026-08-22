using System.Data;
using System.Security.Cryptography;
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
    private static readonly TimeSpan PaymentClaimDuration = TimeSpan.FromMinutes(5);

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

    private static ServiceResult PhoneNumberRequired() => BadRequest(new
    {
        message = "Vui lòng cập nhật số điện thoại trong hồ sơ trước khi đặt sân hoặc thanh toán.",
        errorCode = ApiErrorCodes.PhoneNumberRequired
    });

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
        var currentPlayer = await CurrentPlayerAsync(userId.Value, cancellationToken);
        if (currentPlayer is null) return Forbid();
        if (request.PayerIds.Count == 0 || request.PayerIds.Distinct().Count() != request.PayerIds.Count)
            return BadRequest(new { message = "Danh sách thành viên thanh toán không hợp lệ." });

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

        var now = DateTime.UtcNow;
        ReleaseExpiredPaymentClaims(booking, now);

        var approvedParticipantIds = booking.Match.MatchParticipants
            .Where(IsApprovedMatchParticipant)
            .Select(item => item.PlayerId)
            .ToHashSet();
        var currentParticipantIsApproved = approvedParticipantIds.Contains(currentPlayer.PlayerId);
        var targetParticipantIds = request.PayerIds.ToHashSet();
        if (!currentParticipantIsApproved || !targetParticipantIds.SetEquals(targetParticipantIds.Intersect(approvedParticipantIds)))
            return Forbid();
        if (OmitsAcceptedSponsorship(booking, currentPlayer.PlayerId, targetParticipantIds))
            return BadRequest(new { message = "Các phần đã đồng ý cho bạn trả hộ phải được thanh toán cùng nhau." });
        var currentPaymentIsPending = booking.Payments.Any(item =>
            item.PayerId == currentPlayer.PlayerId
            && item.Status == "Pending"
            && !IsAcceptedSponsorship(item));
        if (currentPaymentIsPending && !targetParticipantIds.Contains(currentPlayer.PlayerId))
            return BadRequest(new { message = "Phần thanh toán của bạn phải được chọn tự động trước khi thanh toán thêm cho người khác." });

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
        if (payments.Any(IsPendingSponsorshipRequest))
            return Conflict(new { message = "Có yêu cầu trả hộ đang chờ phản hồi." });
        if (payments.Any(item => item.PayerId == currentPlayer.PlayerId
            && IsAcceptedSponsorship(item)
            && item.ClaimedByPlayerId != currentPlayer.PlayerId))
            return Conflict(new { message = "Bạn đã đồng ý để thành viên khác trả phần thanh toán này." });
        if (payments.Any(item => item.PayerId != currentPlayer.PlayerId
            && (!IsAcceptedSponsorship(item) || item.ClaimedByPlayerId != currentPlayer.PlayerId)))
            return Conflict(new { message = "Bạn chưa được thành viên đã chọn đồng ý cho trả hộ." });
        if (payments.Any(item => HasActivePaymentClaim(item, now) && item.ClaimedByPlayerId != currentPlayer.PlayerId))
            return Conflict(new { message = "Một phần thanh toán đang được thành viên khác xử lý. Vui lòng tải lại." });
        if (string.IsNullOrWhiteSpace(currentPlayer.PhoneNumber)) return PhoneNumberRequired();
        await EnsurePaymentsHaveConfiguredBankAccountAsync(booking, payments, cancellationToken);
        if (!HasOneConfiguredBankAccount(payments))
            return Conflict(new { message = "Chủ sân chưa cấu hình tài khoản ngân hàng nhận tiền. Vui lòng liên hệ chủ sân để cập nhật tài khoản thanh toán." });
        if (!await HasActiveSePayTokenAsync(booking, cancellationToken))
            return Conflict(new { message = "Chủ sân chưa liên kết SePay để tự động đối soát giao dịch." });

        var totalAmount = payments.Sum(item => item.Amount);
        var currentClaims = booking.Payments
            .Where(item => item.Status == "Pending"
                && item.ClaimedByPlayerId == currentPlayer.PlayerId
                && HasActivePaymentClaim(item, now))
            .ToList();
        var canReuseClaim = currentClaims.Select(item => item.PayerId).ToHashSet().SetEquals(targetParticipantIds)
            && payments.Select(item => item.PaymentGroupId).Distinct().Count() == 1
            && payments[0].PaymentGroupId.HasValue
            && payments.Select(item => item.TransferContent).Distinct().Count() == 1
            && !string.IsNullOrWhiteSpace(payments[0].TransferContent)
            && payments.Select(item => item.QrImageUrl).Distinct().Count() == 1
            && !string.IsNullOrWhiteSpace(payments[0].QrImageUrl);

        if (!canReuseClaim)
        {
            foreach (var payment in currentClaims)
                ClearPaymentAttempt(payment);
        }

        var paymentGroupId = canReuseClaim ? payments[0].PaymentGroupId!.Value : Guid.NewGuid();
        var transferContent = canReuseClaim ? payments[0].TransferContent! : BuildBatchTransferContent(paymentGroupId);
        var qrImageUrl = canReuseClaim
            ? payments[0].QrImageUrl!
            : BuildBatchVietQrUrl(
                payments[0].BankCode!,
                payments[0].BankAccountNumber!,
                payments[0].BankAccountName!,
                totalAmount,
                transferContent);
        var claimExpiresAt = now.Add(PaymentClaimDuration);
        var paymentWindowEnd = booking.HoldExpiresAt
            ?? (booking.HoldRemainingSeconds.HasValue
                ? now.AddSeconds(booking.HoldRemainingSeconds.Value)
                : null);
        if (paymentWindowEnd.HasValue && paymentWindowEnd.Value < claimExpiresAt)
            claimExpiresAt = paymentWindowEnd.Value;

        foreach (var payment in payments)
        {
            payment.PaymentGroupId = paymentGroupId;
            payment.TransferContent = transferContent;
            payment.QrImageUrl = qrImageUrl;
            payment.ClaimedByPlayerId = currentPlayer.PlayerId;
            payment.ClaimExpiresAt = claimExpiresAt;
        }
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var payment in booking.Payments.Where(item => item.Status == "Pending"))
        {
            _paymentRealtime.Publish(new PaymentChangedEvent(
                payment.PaymentId,
                payment.BookingId,
                booking.Court.VenueId,
                payment.Status,
                "PaymentClaimed"));
        }

        var ownerId = booking.Court?.Venue?.OwnerId ?? booking.Slots.FirstOrDefault()?.Court?.Venue?.OwnerId;
        var hasSePayToken = ownerId.HasValue && await _paymentRepository.OwnerBankAccounts.AnyAsync(
            b => b.OwnerId == ownerId.Value && b.IsActive && !string.IsNullOrEmpty(b.SePayApiToken), cancellationToken);

        return Ok(new BatchPaymentPreviewResponse
        {
            BookingId = booking.BookingId,
            PayerIds = payments.Select(item => item.PayerId).ToList(),
            MemberNames = payments.Select(item => item.Payer.User.Username).ToList(),
            TotalAmount = totalAmount,
            TransferContent = transferContent,
            QrImageUrl = qrImageUrl,
            ClaimExpiresAt = claimExpiresAt,
            HasSePayApiToken = hasSePayToken
        });
    }

    public async Task<ServiceResult<PaymentSponsorshipResponse>> RequestPaymentSponsorship(
        int bookingId,
        int targetPlayerId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var currentPlayer = await CurrentPlayerAsync(userId.Value, cancellationToken);
        if (currentPlayer is null) return Forbid();
        if (currentPlayer.PlayerId == targetPlayerId)
            return BadRequest(new { message = "Bạn không thể gửi yêu cầu trả hộ cho chính mình." });
        if (string.IsNullOrWhiteSpace(currentPlayer.PhoneNumber)) return PhoneNumberRequired();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await BatchPaymentBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null || booking.Match is null)
            return NotFound(new { message = "Không tìm thấy booking của trận đấu." });

        if (RebalancePendingMatchPayments(booking))
            await _paymentRepository.SaveChangesAsync(cancellationToken);

        var requester = booking.Match.MatchParticipants.SingleOrDefault(item =>
            item.PlayerId == currentPlayer.PlayerId && IsApprovedMatchParticipant(item));
        var target = booking.Match.MatchParticipants.SingleOrDefault(item =>
            item.PlayerId == targetPlayerId && IsApprovedMatchParticipant(item));
        if (requester is null || target is null) return Forbid();

        var now = DateTime.UtcNow;
        ReleaseExpiredPaymentClaims(booking, now);
        if (booking.Status != "Holding" || !booking.HoldExpiresAt.HasValue || booking.HoldExpiresAt <= now)
            return Conflict(new { message = "Booking không còn trong thời gian giữ chỗ." });

        var payment = booking.Payments.SingleOrDefault(item => item.PayerId == targetPlayerId);
        if (payment is null)
            return NotFound(new { message = "Không tìm thấy phần thanh toán cần trả hộ." });
        if (payment.Status != "Pending")
            return Conflict(new { message = "Phần thanh toán này không còn chờ thanh toán." });
        if (IsAcceptedSponsorship(payment))
        {
            if (payment.ClaimedByPlayerId == currentPlayer.PlayerId)
                return Ok(MapSponsorship(payment, "Accepted"));
            return Conflict(new { message = "Thành viên này đã đồng ý để người khác trả hộ." });
        }
        if (IsPendingSponsorshipRequest(payment))
        {
            if (payment.ClaimedByPlayerId == currentPlayer.PlayerId)
                return Ok(MapSponsorship(payment, "Pending"));
            return Conflict(new { message = "Thành viên này đang xem một yêu cầu trả hộ khác." });
        }
        if (HasActivePaymentClaim(payment, now))
            return Conflict(new { message = "Thành viên này đang tự xử lý phần thanh toán của mình." });

        ClearPaymentClaim(payment);
        payment.AllowPaymentByOthers = false;
        payment.ClaimedByPlayerId = currentPlayer.PlayerId;
        payment.ClaimExpiresAt = null;

        _notifications.Add(new NotificationInput(
            UserId: target.Player.UserId,
            Type: NotificationTypes.Payment,
            Title: "Có yêu cầu trả hộ thanh toán",
            Message: $"{requester.Player.User.Username} muốn trả hộ phần thanh toán của bạn.",
            Tone: NotificationTones.Info,
            LinkTo: $"/checkout?bookingId={bookingId}&matchId={booking.Match.MatchId}",
            LinkLabel: "Đồng ý hoặc từ chối"));

        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishSponsorshipChanged(payment, booking, "PaymentSponsorshipRequested");

        return Ok(MapSponsorship(payment, "Pending"));
    }

    public async Task<ServiceResult<PaymentSponsorshipResponse>> RespondPaymentSponsorship(
        int bookingId,
        RespondPaymentSponsorshipRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var currentPlayer = await CurrentPlayerAsync(userId.Value, cancellationToken);
        if (currentPlayer is null) return Forbid();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await BatchPaymentBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null || booking.Match is null)
            return NotFound(new { message = "Không tìm thấy booking của trận đấu." });
        var now = DateTime.UtcNow;
        if (booking.Status != "Holding" || !booking.HoldExpiresAt.HasValue || booking.HoldExpiresAt <= now)
            return Conflict(new { message = "Booking không còn trong thời gian giữ chỗ." });

        var target = booking.Match.MatchParticipants.SingleOrDefault(item =>
            item.PlayerId == currentPlayer.PlayerId && IsApprovedMatchParticipant(item));
        var payment = booking.Payments.SingleOrDefault(item => item.PayerId == currentPlayer.PlayerId);
        if (target is null || payment is null) return Forbid();
        if (payment.Status != "Pending" || !IsPendingSponsorshipRequest(payment))
            return Conflict(new { message = "Yêu cầu trả hộ không còn hiệu lực." });

        var requestedByPlayerId = payment.ClaimedByPlayerId!.Value;
        var requester = booking.Match.MatchParticipants.SingleOrDefault(item =>
            item.PlayerId == requestedByPlayerId && IsApprovedMatchParticipant(item));
        if (requester is null)
            return Conflict(new { message = "Người gửi yêu cầu không còn trong trận đấu." });

        if (request.Accept)
        {
            payment.AllowPaymentByOthers = true;
            payment.ClaimExpiresAt = null;
        }
        else
        {
            payment.AllowPaymentByOthers = false;
            ClearPaymentClaim(payment);
        }

        var responseStatus = request.Accept ? "Accepted" : "Rejected";
        _notifications.Add(new NotificationInput(
            UserId: requester.Player.UserId,
            Type: NotificationTypes.Payment,
            Title: request.Accept ? "Yêu cầu trả hộ đã được đồng ý" : "Yêu cầu trả hộ đã bị từ chối",
            Message: request.Accept
                ? $"{target.Player.User.Username} đã đồng ý để bạn trả hộ phần thanh toán."
                : $"{target.Player.User.Username} đã từ chối yêu cầu trả hộ của bạn.",
            Tone: request.Accept ? NotificationTones.Success : NotificationTones.Default,
            LinkTo: $"/checkout?bookingId={bookingId}&matchId={booking.Match.MatchId}",
            LinkLabel: request.Accept ? "Tiếp tục thanh toán" : "Xem booking"));

        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishSponsorshipChanged(payment, booking, $"PaymentSponsorship{responseStatus}");

        return Ok(new PaymentSponsorshipResponse
        {
            PaymentId = payment.PaymentId,
            RequestedByPlayerId = requestedByPlayerId,
            TargetPlayerId = payment.PayerId,
            Status = responseStatus
        });
    }

    public async Task<ServiceResult<PaymentSponsorshipResponse>> CancelPaymentSponsorship(
        int bookingId,
        int targetPlayerId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var currentPlayer = await CurrentPlayerAsync(userId.Value, cancellationToken);
        if (currentPlayer is null) return Forbid();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await BatchPaymentBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null || booking.Match is null)
            return NotFound(new { message = "Không tìm thấy booking của trận đấu." });

        var now = DateTime.UtcNow;
        if (booking.Status != "Holding" || !booking.HoldExpiresAt.HasValue || booking.HoldExpiresAt <= now)
            return Conflict(new { message = "Booking không còn trong thời gian giữ chỗ." });

        var requester = booking.Match.MatchParticipants.SingleOrDefault(item =>
            item.PlayerId == currentPlayer.PlayerId && IsApprovedMatchParticipant(item));
        var target = booking.Match.MatchParticipants.SingleOrDefault(item =>
            item.PlayerId == targetPlayerId && IsApprovedMatchParticipant(item));
        var payment = booking.Payments.SingleOrDefault(item => item.PayerId == targetPlayerId);
        if (requester is null || target is null || payment is null) return Forbid();
        if (payment.Status != "Pending" || !IsAcceptedSponsorship(payment) || payment.ClaimedByPlayerId != currentPlayer.PlayerId)
            return Conflict(new { message = "Phần trả hộ này không còn hiệu lực hoặc không thuộc về bạn." });

        var paymentGroupId = payment.PaymentGroupId;
        if (paymentGroupId.HasValue)
        {
            foreach (var groupedPayment in booking.Payments.Where(item =>
                item.Status == "Pending" && item.PaymentGroupId == paymentGroupId))
                ClearPaymentAttempt(groupedPayment);
        }

        payment.AllowPaymentByOthers = false;
        ClearPaymentClaim(payment);
        _notifications.Add(new NotificationInput(
            UserId: target.Player.UserId,
            Type: NotificationTypes.Payment,
            Title: "Yêu cầu trả hộ đã được hủy",
            Message: $"{requester.Player.User.Username} đã hủy trả hộ. Bạn có thể tự thanh toán phần của mình.",
            Tone: NotificationTones.Default,
            LinkTo: $"/checkout?bookingId={bookingId}&matchId={booking.Match.MatchId}",
            LinkLabel: "Tiếp tục thanh toán"));

        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        PublishSponsorshipChanged(payment, booking, "PaymentSponsorshipCancelled");

        return Ok(new PaymentSponsorshipResponse
        {
            PaymentId = payment.PaymentId,
            RequestedByPlayerId = currentPlayer.PlayerId,
            TargetPlayerId = targetPlayerId,
            Status = "Cancelled"
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
        var currentPlayer = await CurrentPlayerAsync(userId.Value, cancellationToken);
        if (currentPlayer is null) return Forbid();
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

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await BatchPaymentBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null || booking.Match is null)
            return NotFound(new { message = "Không tìm thấy booking của trận đấu." });

        if (RebalancePendingMatchPayments(booking))
            await _paymentRepository.SaveChangesAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var approvedParticipantIds = booking.Match.MatchParticipants
            .Where(IsApprovedMatchParticipant)
            .Select(item => item.PlayerId)
            .ToHashSet();
        var currentParticipantIsApproved = approvedParticipantIds.Contains(currentPlayer.PlayerId);
        var targetParticipantIds = request.PayerIds.ToHashSet();
        if (!currentParticipantIsApproved || !targetParticipantIds.SetEquals(targetParticipantIds.Intersect(approvedParticipantIds)))
            return Forbid();
        if (OmitsAcceptedSponsorship(booking, currentPlayer.PlayerId, targetParticipantIds))
            return BadRequest(new { message = "Các phần đã đồng ý cho bạn trả hộ phải được thanh toán cùng nhau." });
        var currentPaymentIsPending = booking.Payments.Any(item =>
            item.PayerId == currentPlayer.PlayerId
            && item.Status == "Pending"
            && !IsAcceptedSponsorship(item));
        if (currentPaymentIsPending && !targetParticipantIds.Contains(currentPlayer.PlayerId))
            return BadRequest(new { message = "Phần thanh toán của bạn phải được chọn tự động trước khi thanh toán thêm cho người khác." });

        var payments = booking.Payments
            .Where(item => targetParticipantIds.Contains(item.PayerId))
            .OrderBy(item => item.PayerId)
            .ToList();
        if (payments.Count != targetParticipantIds.Count)
            return NotFound(new { message = "Không tìm thấy đầy đủ khoản thanh toán đã chọn." });
        if (booking.Status != "Holding" || !booking.HoldExpiresAt.HasValue || booking.HoldExpiresAt <= now)
            return Conflict(new { message = "Booking không còn trong thời gian giữ chỗ." });
        if (payments.Any(item => item.Status != "Pending"))
            return Conflict(new { message = "Một hoặc nhiều phần đã được gửi hoặc thanh toán. Vui lòng tải lại." });
        if (payments.Any(IsPendingSponsorshipRequest))
            return Conflict(new { message = "Có yêu cầu trả hộ đang chờ phản hồi." });
        if (payments.Any(item => item.PayerId == currentPlayer.PlayerId
            && IsAcceptedSponsorship(item)
            && item.ClaimedByPlayerId != currentPlayer.PlayerId))
            return Conflict(new { message = "Bạn đã đồng ý để thành viên khác trả phần thanh toán này." });
        if (payments.Any(item => item.PayerId != currentPlayer.PlayerId
            && (!IsAcceptedSponsorship(item) || item.ClaimedByPlayerId != currentPlayer.PlayerId)))
            return Conflict(new { message = "Bạn chưa được thành viên đã chọn đồng ý cho trả hộ." });
        if (payments.Any(item => !HasActivePaymentClaim(item, now) || item.ClaimedByPlayerId != currentPlayer.PlayerId))
            return Conflict(new { message = "Quyền giữ phần thanh toán đã hết hạn hoặc thuộc thành viên khác. Vui lòng tải lại mã QR." });
        if (payments.Select(item => item.PaymentGroupId).Distinct().Count() != 1
            || !payments[0].PaymentGroupId.HasValue
            || payments.Select(item => item.TransferContent).Distinct().Count() != 1
            || string.IsNullOrWhiteSpace(payments[0].TransferContent))
            return Conflict(new { message = "Nhóm thanh toán không còn hợp lệ. Vui lòng tạo lại mã QR." });
        if (string.IsNullOrWhiteSpace(currentPlayer.PhoneNumber)) return PhoneNumberRequired();
        await EnsurePaymentsHaveConfiguredBankAccountAsync(booking, payments, cancellationToken);
        if (!HasOneConfiguredBankAccount(payments))
            return Conflict(new { message = "Chủ sân chưa cấu hình tài khoản ngân hàng nhận tiền. Vui lòng liên hệ chủ sân để cập nhật tài khoản thanh toán." });
        if (!await HasActiveSePayTokenAsync(booking, cancellationToken))
            return Conflict(new { message = "Chủ sân chưa liên kết SePay để tự động đối soát giao dịch." });

        var paymentGroupId = payments[0].PaymentGroupId!.Value;
        var transferContent = payments[0].TransferContent!;
        string receiptUrl;
        try
        {
            receiptUrl = await SaveReceiptAsync(booking.BookingId, receipt, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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
            payment.PaymentGroupId = paymentGroupId;
            if (IsAcceptedSponsorship(payment))
                payment.ClaimExpiresAt = null;
            else
            {
                payment.ClaimedByPlayerId = null;
                payment.ClaimExpiresAt = null;
            }
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

        booking.HoldRemainingSeconds = null;
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
            PaymentGroupId = paymentGroupId,
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
        var player = await CurrentPlayerAsync(userId.Value, cancellationToken);
        if (player is null) return Forbid();

        var receipt = request.Receipt;
        if (receipt is null || receipt.Length == 0)
            return BadRequest(new { message = "Vui lòng tải ảnh biên lai." });
        if (receipt.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "Ảnh biên lai không được vượt quá 5 MB." });
        if (!AllowedReceiptTypes.Contains(receipt.ContentType))
            return BadRequest(new { message = "Biên lai chỉ hỗ trợ JPG, PNG hoặc WEBP." });
        if (!await ImageUploadPolicy.HasValidSignatureAsync(receipt, cancellationToken))
            return BadRequest(new { message = "Nội dung tệp biên lai không khớp với định dạng ảnh." });

        if (request.PayerId.HasValue && request.PayerId != player.PlayerId) return Forbid();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await BaseBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && !item.MatchId.HasValue, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking cần thanh toán." });
        if (booking.PlayerId != player.PlayerId) return Forbid();
        if (booking.Status != "Holding" || !booking.HoldExpiresAt.HasValue || booking.HoldExpiresAt <= DateTime.UtcNow)
            return Conflict(new { message = "Booking không còn trong thời gian giữ chỗ." });

        var payment = booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault();
        if (payment is null) return NotFound(new { message = "Không tìm thấy khoản thanh toán của booking." });
        if (payment.PayerId != player.PlayerId) return Forbid();
        if (payment.Status != "Pending")
            return Conflict(new { message = "Khoản thanh toán không còn ở trạng thái chờ gửi biên lai." });
        if (string.IsNullOrWhiteSpace(player.PhoneNumber)) return PhoneNumberRequired();
        if (string.IsNullOrWhiteSpace(payment.BankCode)
            || string.IsNullOrWhiteSpace(payment.BankAccountNumber)
            || string.IsNullOrWhiteSpace(payment.BankAccountName))
            return Conflict(new { message = "Sân chưa cấu hình tài khoản nhận tiền hợp lệ." });
        if (!await HasActiveSePayTokenAsync(booking, cancellationToken))
            return Conflict(new { message = "Chủ sân chưa liên kết SePay để tự động đối soát giao dịch." });

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
        if (!await _paymentRepository.OwnerBankAccounts.AnyAsync(
                item => item.OwnerId == ticket.TicketSession.Booking.Court.Venue.OwnerId
                    && item.IsActive && !string.IsNullOrEmpty(item.SePayApiToken), cancellationToken))
            return Conflict(new { message = "Owner chưa liên kết SePay để tự động đối soát giao dịch." });

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

    public async Task<ServiceResult<CheckoutBookingContextResponse>> GetCheckoutBookingContext(
        int bookingId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var context = await _paymentRepository.Payments.AsNoTracking()
            .Where(item => item.BookingId == bookingId && item.Payer.UserId == userId.Value)
            .OrderByDescending(item => item.PaymentId)
            .Select(item => new CheckoutBookingContextResponse { MatchId = item.Booking.MatchId })
            .FirstOrDefaultAsync(cancellationToken);

        return context is null
            ? NotFound(new { message = "Không tìm thấy quyền thanh toán của booking." })
            : Ok(context);
    }

    public async Task<ServiceResult<BankTransferResponse>> GetPlayerBookingPayment(
        int bookingId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var player = await CurrentPlayerAsync(userId.Value, cancellationToken);
        if (player is null) return Forbid();

        var payment = await BasePaymentQuery(asTracking: false)
            .Where(item => item.BookingId == bookingId
                && item.Booking.Player != null
                && item.Booking.Player.UserId == userId.Value)
            .OrderByDescending(item => item.PaymentId)
            .FirstOrDefaultAsync(cancellationToken);
        if (payment is null) return NotFound(new { message = "Không tìm thấy khoản thanh toán của booking." });
        if (string.IsNullOrWhiteSpace(player.PhoneNumber)) return PhoneNumberRequired();

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

    public async Task<ServiceResult<List<BankTransferResponse>>> MarkMatchRefundSent(
        int paymentId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var target = await _paymentRepository.Payments.AsNoTracking()
            .Where(item => item.PaymentId == paymentId
                && item.Booking.MatchId.HasValue
                && item.Booking.Court.Venue.Owner.UserId == userId.Value)
            .Select(item => new { item.BookingId, item.Booking.Court.Venue.OwnerId })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null) return NotFound(new { message = "Không tìm thấy khoản hoàn tiền của trận ghép." });

        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{target.BookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await BaseBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == target.BookingId
                && item.MatchId.HasValue
                && item.Court.Venue.OwnerId == target.OwnerId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking thuộc khoản hoàn tiền." });

        var selectedPayment = booking.Payments.Single(item => item.PaymentId == paymentId);
        var groupPayments = booking.Payments
            .Where(item => selectedPayment.PaymentGroupId.HasValue
                ? item.PaymentGroupId == selectedPayment.PaymentGroupId
                : item.PaymentId == paymentId)
            .OrderBy(item => item.PaymentId)
            .ToList();
        var refundPayments = groupPayments.Where(item => item.Status == "RefundPending").ToList();
        if (refundPayments.Count == 0)
            return Conflict(new { message = "Giao dịch này không có khoản nào đang chờ hoàn tiền." });
        if (refundPayments.All(item => item.StatusHistories.Any(history => history.Action == "OwnerMarkedRefundSent")))
            return Ok(groupPayments.Select(item => MapSubmittedTransfer(item, booking)).ToList());

        var recipientPlayerIds = refundPayments
            .Select(item => item.ClaimedByPlayerId ?? item.PayerId)
            .Distinct()
            .ToList();
        if (recipientPlayerIds.Count != 1)
            return Conflict(new { message = "Giao dịch hoàn tiền không có một người nhận duy nhất." });

        var recipient = await _paymentRepository.Players
            .Include(item => item.User)
            .SingleOrDefaultAsync(item => item.PlayerId == recipientPlayerIds[0], cancellationToken);
        if (recipient is null) return NotFound(new { message = "Không tìm thấy người chơi nhận hoàn tiền." });

        var now = DateTime.UtcNow;
        foreach (var payment in refundPayments.Where(item =>
            !item.StatusHistories.Any(history => history.Action == "OwnerMarkedRefundSent")))
        {
            payment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = "RefundPending",
                ToStatus = "RefundPending",
                Action = "OwnerMarkedRefundSent",
                Reason = "Chủ sân xác nhận đã chuyển tiền hoàn và đang chờ người chơi xác nhận.",
                ActorUserId = userId.Value,
                CreatedAt = now
            });
        }

        var refundAmount = refundPayments.Sum(item => item.Amount);
        _notifications.Add(new NotificationInput(
            UserId: recipient.UserId,
            Type: NotificationTypes.Payment,
            Title: "Bạn đã nhận được tiền hoàn chưa?",
            Message: $"Chủ sân thông báo đã hoàn {refundAmount:0} đ cho booking {booking.BookingCode ?? $"#{booking.BookingId}"}. Vui lòng xác nhận sau khi tiền đã vào tài khoản.",
            Tone: NotificationTones.Urgent,
            LinkTo: $"/notifications?confirmRefundPaymentId={selectedPayment.PaymentId}",
            LinkLabel: "Đã nhận được tiền"));
        await _paymentRepository.AddAuditLogAsync(
            NewAudit(booking.Court.VenueId, userId.Value, $"MatchRefundSent:{selectedPayment.PaymentId}:{refundPayments.Count}"),
            cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _notifications.PublishPending();
        foreach (var payment in refundPayments)
        {
            _paymentRealtime.Publish(new PaymentChangedEvent(
                payment.PaymentId, payment.BookingId, booking.Court.VenueId, payment.Status, "RefundSent"));
        }
        _matchRealtime.Publish(booking.MatchId!.Value, "RefundSent");

        return Ok(groupPayments.Select(item => MapSubmittedTransfer(item, booking)).ToList());
    }

    public async Task<ServiceResult<List<BankTransferResponse>>> ConfirmMatchRefundReceived(
        int paymentId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var player = await _paymentRepository.Players.AsNoTracking()
            .Where(item => item.UserId == userId.Value)
            .Select(item => new { item.PlayerId, item.User.Username })
            .SingleOrDefaultAsync(cancellationToken);
        if (player is null) return Forbid();

        await using var transaction = await _paymentRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var target = await _paymentRepository.Payments.AsNoTracking()
            .Where(item => item.PaymentId == paymentId && item.Booking.MatchId.HasValue)
            .Select(item => new
            {
                item.BookingId,
                RefundRecipientPlayerId = item.ClaimedByPlayerId ?? item.PayerId
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null) return NotFound(new { message = "Không tìm thấy khoản hoàn tiền của trận ghép." });
        if (target.RefundRecipientPlayerId != player.PlayerId) return Forbid();

        if (!await SqlServerBookingLock.AcquireAsync(transaction, $"booking-payment:{target.BookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await BaseBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == target.BookingId && item.MatchId.HasValue, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking thuộc khoản hoàn tiền." });

        var selectedPayment = booking.Payments.Single(item => item.PaymentId == paymentId);
        var groupPayments = booking.Payments
            .Where(item => selectedPayment.PaymentGroupId.HasValue
                ? item.PaymentGroupId == selectedPayment.PaymentGroupId
                : item.PaymentId == paymentId)
            .OrderBy(item => item.PaymentId)
            .ToList();
        var refundPayments = groupPayments.Where(item => item.Status == "RefundPending").ToList();
        if (refundPayments.Count == 0)
        {
            return groupPayments.All(item => item.Status == "Refunded")
                ? Ok(groupPayments.Select(item => MapSubmittedTransfer(item, booking)).ToList())
                : Conflict(new { message = "Giao dịch này không còn chờ xác nhận hoàn tiền." });
        }
        if (refundPayments.Any(item => (item.ClaimedByPlayerId ?? item.PayerId) != player.PlayerId))
            return Forbid();
        if (refundPayments.Any(item => !item.StatusHistories.Any(history => history.Action == "OwnerMarkedRefundSent")))
            return Conflict(new { message = "Chủ sân chưa xác nhận đã chuyển khoản hoàn tiền." });

        var now = DateTime.UtcNow;
        foreach (var payment in refundPayments)
        {
            payment.Status = "Refunded";
            payment.StatusHistories.Add(new PaymentStatusHistory
            {
                FromStatus = "RefundPending",
                ToStatus = "Refunded",
                Action = "PlayerConfirmedRefund",
                Reason = "Người chơi xác nhận đã nhận được tiền hoàn.",
                ActorUserId = userId.Value,
                CreatedAt = now
            });
        }

        _notifications.Add(new NotificationInput(
            UserId: booking.Court.Venue.Owner.UserId,
            Type: NotificationTypes.Payment,
            Title: "Người chơi đã xác nhận nhận tiền hoàn",
            Message: $"{player.Username} đã xác nhận nhận đủ tiền hoàn cho booking {booking.BookingCode ?? $"#{booking.BookingId}"}.",
            Tone: NotificationTones.Success,
            LinkTo: "/owner/match-bookings",
            LinkLabel: "Xem booking"));
        await _paymentRepository.AddAuditLogAsync(
            NewAudit(booking.Court.VenueId, userId.Value, $"MatchRefundConfirmed:{selectedPayment.PaymentId}:{refundPayments.Count}"),
            cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _notifications.PublishPending();
        foreach (var payment in refundPayments)
        {
            _paymentRealtime.Publish(new PaymentChangedEvent(
                payment.PaymentId, payment.BookingId, booking.Court.VenueId, payment.Status, "RefundConfirmed"));
        }
        _matchRealtime.Publish(booking.MatchId!.Value, "RefundConfirmed");

        return Ok(groupPayments.Select(item => MapSubmittedTransfer(item, booking)).ToList());
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
        else if (!booking.MatchId.HasValue)
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

    private Task<Player?> CurrentPlayerAsync(int userId, CancellationToken cancellationToken) =>
        _paymentRepository.Players.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

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
            .Include(item => item.Booking).ThenInclude(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner).ThenInclude(item => item.BankAccounts)
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
            .Include(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner).ThenInclude(item => item.BankAccounts)
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
            .Include(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner).ThenInclude(item => item.BankAccounts)
            .Include(item => item.Slots).ThenInclude(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.Owner).ThenInclude(item => item.BankAccounts)
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

    private async Task<bool> HasActiveSePayTokenAsync(Booking booking, CancellationToken cancellationToken)
    {
        var ownerId = booking.Court?.Venue?.OwnerId
            ?? booking.Slots.FirstOrDefault()?.Court?.Venue?.OwnerId;
        return ownerId.HasValue && await _paymentRepository.OwnerBankAccounts.AnyAsync(
            item => item.OwnerId == ownerId.Value && item.IsActive && !string.IsNullOrEmpty(item.SePayApiToken),
            cancellationToken);
    }
    private static bool HasActivePaymentClaim(Payment payment, DateTime now) =>
        payment.ClaimedByPlayerId.HasValue
        && payment.ClaimExpiresAt.HasValue
        && payment.ClaimExpiresAt.Value > now;

    private static bool IsPendingSponsorshipRequest(Payment payment) =>
        !payment.AllowPaymentByOthers
        && payment.ClaimedByPlayerId.HasValue
        && payment.ClaimedByPlayerId != payment.PayerId
        && !payment.ClaimExpiresAt.HasValue;

    private static bool IsAcceptedSponsorship(Payment payment) =>
        payment.AllowPaymentByOthers
        && payment.ClaimedByPlayerId.HasValue
        && payment.ClaimedByPlayerId != payment.PayerId;

    private static bool OmitsAcceptedSponsorship(Booking booking, int sponsorPlayerId, IReadOnlySet<int> selectedPlayerIds) =>
        booking.Payments.Any(item =>
            item.Status == "Pending"
            && IsAcceptedSponsorship(item)
            && item.ClaimedByPlayerId == sponsorPlayerId
            && !selectedPlayerIds.Contains(item.PayerId));

    private static PaymentSponsorshipResponse MapSponsorship(Payment payment, string status) => new()
    {
        PaymentId = payment.PaymentId,
        RequestedByPlayerId = payment.ClaimedByPlayerId!.Value,
        TargetPlayerId = payment.PayerId,
        Status = status
    };

    private void PublishSponsorshipChanged(Payment payment, Booking booking, string action)
    {
        _paymentRealtime.Publish(new PaymentChangedEvent(
            payment.PaymentId,
            payment.BookingId,
            booking.Court.VenueId,
            payment.Status,
            action));
        _matchRealtime.Publish(booking.MatchId!.Value, action);
    }

    private static void ClearPaymentClaim(Payment payment)
    {
        payment.ClaimedByPlayerId = null;
        payment.ClaimExpiresAt = null;
        payment.PaymentGroupId = null;
        payment.TransferContent = null;
        payment.QrImageUrl = null;
    }

    private static void ClearPaymentAttempt(Payment payment)
    {
        if (!IsAcceptedSponsorship(payment)) payment.ClaimedByPlayerId = null;
        payment.ClaimExpiresAt = null;
        payment.PaymentGroupId = null;
        payment.TransferContent = null;
        payment.QrImageUrl = null;
    }

    private static void ReleaseExpiredPaymentClaims(Booking booking, DateTime now)
    {
        foreach (var payment in booking.Payments.Where(item =>
            item.Status == "Pending"
            && item.ClaimedByPlayerId.HasValue
            && item.ClaimExpiresAt.HasValue
            && item.ClaimExpiresAt.Value <= now))
            ClearPaymentAttempt(payment);
    }

    private static string BuildBatchTransferContent(Guid paymentGroupId) =>
        $"PLG-{paymentGroupId:N}".ToUpperInvariant();

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

    private static void ResumeBookingHoldIfNoPendingReview(Booking booking, DateTime now)
    {
        if (booking.Status != "Holding"
            || booking.HoldExpiresAt.HasValue
            || booking.Payments.Any(payment => payment.Status == "WaitingForConfirmation"))
            return;

        // ponytail: missing saved time expires now; never grant a fresh hold after rejection.
        booking.HoldExpiresAt = booking.HoldRemainingSeconds.HasValue
            ? now.AddSeconds(Math.Max(1, booking.HoldRemainingSeconds.Value))
            : now;
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
        PlayerName = payment.Payer?.User.Username ?? booking.Player?.User.Username ?? string.Empty,
        PlayerPhoneNumber = payment.Payer?.PhoneNumber ?? booking.Player?.PhoneNumber,
        PayerId = payment.PayerId,
        PayerUserId = payment.Payer?.UserId ?? booking.Player?.UserId,
        HasSePayApiToken = booking.Court?.Venue?.Owner?.BankAccounts?.Any(a => a.IsActive && !string.IsNullOrEmpty(a.SePayApiToken))
            ?? booking.Slots.FirstOrDefault()?.Court?.Venue?.Owner?.BankAccounts?.Any(a => a.IsActive && !string.IsNullOrEmpty(a.SePayApiToken))
            ?? false,
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
