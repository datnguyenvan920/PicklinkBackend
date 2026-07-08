using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Matches;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Payments;
public sealed record PaymentServiceDependencies(ApplicationDbContext DbContext, IWebHostEnvironment Environment, IConfiguration Configuration, ScheduleRealtimeNotifier ScheduleRealtime, PaymentRealtimeNotifier PaymentRealtime, MatchRealtimeNotifier MatchRealtime, NotificationService Notifications);

public class PaymentService
{
    private static readonly HashSet<string> AllowedReceiptTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly PaymentRealtimeNotifier _paymentRealtime;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private readonly NotificationService _notifications;

    public PaymentService(
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ScheduleRealtimeNotifier scheduleRealtime,
        PaymentRealtimeNotifier paymentRealtime,
        MatchRealtimeNotifier matchRealtime,
        NotificationService notifications)
    {
        _dbContext = dbContext;
        _environment = environment;
        _configuration = configuration;
        _scheduleRealtime = scheduleRealtime;
        _paymentRealtime = paymentRealtime;
        _matchRealtime = matchRealtime;
        _notifications = notifications;
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
        var account = await _dbContext.OwnerBankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == owner.OwnerId, cancellationToken);
        return account is null ? NotFound(new { message = "ChÃƒÂ¡Ã‚Â»Ã‚Â§ sÃƒÆ’Ã‚Â¢n chÃƒâ€ Ã‚Â°a cÃƒÂ¡Ã‚ÂºÃ‚Â¥u hÃƒÆ’Ã‚Â¬nh tÃƒÆ’Ã‚Â i khoÃƒÂ¡Ã‚ÂºÃ‚Â£n nhÃƒÂ¡Ã‚ÂºÃ‚Â­n tiÃƒÂ¡Ã‚Â»Ã‚Ân." }) : Ok(MapAccount(account));
    }
    public async Task<ServiceResult<OwnerBankAccountResponse>> UpsertBankAccount(
        OwnerBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        var owner = await CurrentOwnerAsync(cancellationToken);
        if (owner is null) return Forbid();
        var account = await _dbContext.OwnerBankAccounts.SingleOrDefaultAsync(item => item.OwnerId == owner.OwnerId, cancellationToken);
        if (account is null)
        {
            account = new OwnerBankAccount { OwnerId = owner.OwnerId, CreatedAt = DateTime.UtcNow };
            _dbContext.OwnerBankAccounts.Add(account);
        }

        account.BankCode = request.BankCode.Trim().ToUpperInvariant();
        account.BankName = request.BankName.Trim();
        account.AccountNumber = request.AccountNumber.Trim();
        account.AccountHolderName = request.AccountHolderName.Trim().ToUpperInvariant();
        account.IsActive = true;
        account.UpdatedAt = DateTime.UtcNow;
        foreach (var venueId in await _dbContext.Venues.Where(item => item.OwnerId == owner.OwnerId).Select(item => item.VenueId).ToListAsync(cancellationToken))
            _dbContext.VenueAuditLogs.Add(NewAudit(venueId, "BankAccountUpdated"));
        await _dbContext.SaveChangesAsync(cancellationToken);
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
            return BadRequest(new { message = "Danh sÃƒÆ’Ã‚Â¡ch thÃƒÆ’Ã‚Â nh viÃƒÆ’Ã‚Âªn thanh toÃƒÆ’Ã‚Â¡n khÃƒÆ’Ã‚Â´ng hÃƒÂ¡Ã‚Â»Ã‚Â£p lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡." });

        var currentPlayerId = await _dbContext.Players
            .Where(item => item.UserId == userId.Value)
            .Select(item => (int?)item.PlayerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (currentPlayerId is null) return Forbid();

        var booking = await BatchPaymentBookingQuery(asTracking: false)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null || booking.Match is null)
            return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y booking cÃƒÂ¡Ã‚Â»Ã‚Â§a trÃƒÂ¡Ã‚ÂºÃ‚Â­n Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â¥u." });

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
            return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â§y Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã‚Â§ khoÃƒÂ¡Ã‚ÂºÃ‚Â£n thanh toÃƒÆ’Ã‚Â¡n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ chÃƒÂ¡Ã‚Â»Ã‚Ân." });
        if (booking.Status != "Holding" || booking.HoldExpiresAt <= DateTime.UtcNow)
            return Conflict(new { message = "Booking khÃƒÆ’Ã‚Â´ng cÃƒÆ’Ã‚Â²n trong thÃƒÂ¡Ã‚Â»Ã‚Âi gian giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€." });
        if (payments.Any(item => item.Status != "Pending"))
            return Conflict(new { message = "MÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢t hoÃƒÂ¡Ã‚ÂºÃ‚Â·c nhiÃƒÂ¡Ã‚Â»Ã‚Âu phÃƒÂ¡Ã‚ÂºÃ‚Â§n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c gÃƒÂ¡Ã‚Â»Ã‚Â­i hoÃƒÂ¡Ã‚ÂºÃ‚Â·c thanh toÃƒÆ’Ã‚Â¡n. Vui lÃƒÆ’Ã‚Â²ng tÃƒÂ¡Ã‚ÂºÃ‚Â£i lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });
        if (!HasOneConfiguredBankAccount(payments))
            return Conflict(new { message = "CÃƒÆ’Ã‚Â¡c khoÃƒÂ¡Ã‚ÂºÃ‚Â£n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ chÃƒÂ¡Ã‚Â»Ã‚Ân khÃƒÆ’Ã‚Â´ng cÃƒÆ’Ã‚Â³ cÃƒÆ’Ã‚Â¹ng tÃƒÆ’Ã‚Â i khoÃƒÂ¡Ã‚ÂºÃ‚Â£n nhÃƒÂ¡Ã‚ÂºÃ‚Â­n tiÃƒÂ¡Ã‚Â»Ã‚Ân hÃƒÂ¡Ã‚Â»Ã‚Â£p lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡." });

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
            return BadRequest(new { message = "Danh sÃƒÆ’Ã‚Â¡ch thÃƒÆ’Ã‚Â nh viÃƒÆ’Ã‚Âªn thanh toÃƒÆ’Ã‚Â¡n khÃƒÆ’Ã‚Â´ng hÃƒÂ¡Ã‚Â»Ã‚Â£p lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡." });
        if (receipt is null || receipt.Length == 0)
            return BadRequest(new { message = "Vui lÃƒÆ’Ã‚Â²ng tÃƒÂ¡Ã‚ÂºÃ‚Â£i ÃƒÂ¡Ã‚ÂºÃ‚Â£nh biÃƒÆ’Ã‚Âªn lai." });
        if (receipt.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "ÃƒÂ¡Ã‚ÂºÃ‚Â¢nh biÃƒÆ’Ã‚Âªn lai khÃƒÆ’Ã‚Â´ng Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c vÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£t quÃƒÆ’Ã‚Â¡ 5 MB." });
        if (!AllowedReceiptTypes.Contains(receipt.ContentType))
            return BadRequest(new { message = "BiÃƒÆ’Ã‚Âªn lai chÃƒÂ¡Ã‚Â»Ã¢â‚¬Â° hÃƒÂ¡Ã‚Â»Ã¢â‚¬â€ trÃƒÂ¡Ã‚Â»Ã‚Â£ JPG, PNG hoÃƒÂ¡Ã‚ÂºÃ‚Â·c WEBP." });

        var currentPlayerId = await _dbContext.Players
            .Where(item => item.UserId == userId.Value)
            .Select(item => (int?)item.PlayerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (currentPlayerId is null) return Forbid();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÆ’Ã‚Â½. Vui lÃƒÆ’Ã‚Â²ng thÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });

        var booking = await BatchPaymentBookingQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (booking is null || booking.Match is null)
            return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y booking cÃƒÂ¡Ã‚Â»Ã‚Â§a trÃƒÂ¡Ã‚ÂºÃ‚Â­n Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â¥u." });
        if (!await SqlServerBookingLock.AcquireAsync(
                _dbContext,
                transaction,
                $"match-roster:{booking.Match.MatchId}",
                cancellationToken))
            return Conflict(new { message = "TrÃƒÂ¡Ã‚ÂºÃ‚Â­n Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c cÃƒÂ¡Ã‚ÂºÃ‚Â­p nhÃƒÂ¡Ã‚ÂºÃ‚Â­t. Vui lÃƒÆ’Ã‚Â²ng thÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });

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
            return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â§y Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã‚Â§ khoÃƒÂ¡Ã‚ÂºÃ‚Â£n thanh toÃƒÆ’Ã‚Â¡n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ chÃƒÂ¡Ã‚Â»Ã‚Ân." });
        if (booking.Status != "Holding" || booking.HoldExpiresAt <= DateTime.UtcNow)
            return Conflict(new { message = "Booking khÃƒÆ’Ã‚Â´ng cÃƒÆ’Ã‚Â²n trong thÃƒÂ¡Ã‚Â»Ã‚Âi gian giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€." });
        if (payments.Any(item => item.Status != "Pending"))
            return Conflict(new { message = "MÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢t hoÃƒÂ¡Ã‚ÂºÃ‚Â·c nhiÃƒÂ¡Ã‚Â»Ã‚Âu phÃƒÂ¡Ã‚ÂºÃ‚Â§n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c gÃƒÂ¡Ã‚Â»Ã‚Â­i hoÃƒÂ¡Ã‚ÂºÃ‚Â·c thanh toÃƒÆ’Ã‚Â¡n. Vui lÃƒÆ’Ã‚Â²ng tÃƒÂ¡Ã‚ÂºÃ‚Â£i lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });
        if (!HasOneConfiguredBankAccount(payments))
            return Conflict(new { message = "CÃƒÆ’Ã‚Â¡c khoÃƒÂ¡Ã‚ÂºÃ‚Â£n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ chÃƒÂ¡Ã‚Â»Ã‚Ân khÃƒÆ’Ã‚Â´ng cÃƒÆ’Ã‚Â³ cÃƒÆ’Ã‚Â¹ng tÃƒÆ’Ã‚Â i khoÃƒÂ¡Ã‚ÂºÃ‚Â£n nhÃƒÂ¡Ã‚ÂºÃ‚Â­n tiÃƒÂ¡Ã‚Â»Ã‚Ân hÃƒÂ¡Ã‚Â»Ã‚Â£p lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡." });

        var paymentGroupId = Guid.NewGuid();
        var receiptUrl = await SaveBatchReceiptAsync(paymentGroupId, receipt, cancellationToken);
        var submittedAt = DateTime.UtcNow;
        var transferContent = BuildBatchTransferContent(booking, targetParticipantIds);
        var totalAmount = payments.Sum(item => item.Amount);
        var qrImageUrl = BuildBatchVietQrUrl(
            payments[0].BankCode!,
            payments[0].BankAccountNumber!,
            payments[0].BankAccountName!,
            totalAmount,
            transferContent);
        foreach (var payment in payments)
        {
            payment.PaymentGroupId = paymentGroupId;
            payment.ReceiptImageUrl = receiptUrl;
            payment.SubmittedAt = submittedAt;
            payment.TransferContent = transferContent;
            payment.QrImageUrl = qrImageUrl;
            payment.Status = "WaitingForConfirmation";
            payment.RejectionReason = null;
            payment.StatusHistories.Add(NewHistory(
                "Pending",
                payment.Status,
                "BatchSubmitted",
                $"GÃƒÂ¡Ã‚Â»Ã‚Â­i chung biÃƒÆ’Ã‚Âªn lai cho {payments.Count} thÃƒÆ’Ã‚Â nh viÃƒÆ’Ã‚Âªn",
                userId));
            _dbContext.VenueAuditLogs.Add(NewAudit(
                booking.Court.VenueId,
                $"BatchPaymentSubmitted:{paymentGroupId}:{payment.PaymentId}"));
        }

        var reviewMinutes = Math.Clamp(
            _configuration.GetValue("Payment:ReviewMinutes", 1440),
            15,
            10080);
        var reviewDeadline = DateTime.UtcNow.AddMinutes(reviewMinutes);
        if (!booking.HoldExpiresAt.HasValue || booking.HoldExpiresAt < reviewDeadline)
            booking.HoldExpiresAt = reviewDeadline;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            booking.Court.VenueId,
            booking.CourtId,
            booking.StartTime,
            booking.EndTime,
            booking.Status,
            "Updated"));
        foreach (var payment in payments)
            PublishPaymentChanged(payment, "Submitted");

        return Ok(new BatchPaymentResponse
        {
            PaymentGroupId = paymentGroupId,
            TotalAmount = totalAmount,
            Payments = payments.Select(MapPayment).ToList()
        });
    }
    public async Task<ServiceResult<BankTransferResponse>> SubmitTransfer(
        int bookingId,
        SubmitPaymentReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var receipt = request.Receipt;
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (receipt is null || receipt.Length == 0) return BadRequest(new { message = "Vui lÃƒÆ’Ã‚Â²ng tÃƒÂ¡Ã‚ÂºÃ‚Â£i ÃƒÂ¡Ã‚ÂºÃ‚Â£nh biÃƒÆ’Ã‚Âªn lai." });
        if (receipt.Length > 5 * 1024 * 1024) return BadRequest(new { message = "ÃƒÂ¡Ã‚ÂºÃ‚Â¢nh biÃƒÆ’Ã‚Âªn lai khÃƒÆ’Ã‚Â´ng Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c vÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£t quÃƒÆ’Ã‚Â¡ 5 MB." });
        if (!AllowedReceiptTypes.Contains(receipt.ContentType)) return BadRequest(new { message = "BiÃƒÆ’Ã‚Âªn lai chÃƒÂ¡Ã‚Â»Ã¢â‚¬Â° hÃƒÂ¡Ã‚Â»Ã¢â‚¬â€ trÃƒÂ¡Ã‚Â»Ã‚Â£ JPG, PNG hoÃƒÂ¡Ã‚ÂºÃ‚Â·c WEBP." });

        var currentPlayerId = await _dbContext.Players
            .Where(item => item.UserId == userId.Value)
            .Select(item => (int?)item.PlayerId)
            .SingleOrDefaultAsync(cancellationToken);
        var targetPayerId = request.PayerId ?? currentPlayerId;
        if (targetPayerId is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y yÃƒÆ’Ã‚Âªu cÃƒÂ¡Ã‚ÂºÃ‚Â§u thanh toÃƒÆ’Ã‚Â¡n." });

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÆ’Ã‚Â½. Vui lÃƒÆ’Ã‚Â²ng thÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });

        var payment = await PaymentSubmissionQuery()
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.PayerId == targetPayerId, cancellationToken);
        if (payment?.Booking.MatchId is int matchId
            && !await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "TrÃƒÂ¡Ã‚ÂºÃ‚Â­n Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c cÃƒÂ¡Ã‚ÂºÃ‚Â­p nhÃƒÂ¡Ã‚ÂºÃ‚Â­t. Vui lÃƒÆ’Ã‚Â²ng thÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });
        if (payment is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y yÃƒÆ’Ã‚Âªu cÃƒÂ¡Ã‚ÂºÃ‚Â§u thanh toÃƒÆ’Ã‚Â¡n." });
        if (payment.Booking.Match is not null)
        {
            var currentParticipantIsApproved = currentPlayerId.HasValue
                && payment.Booking.Match.MatchParticipants.Any(item => item.PlayerId == currentPlayerId.Value && IsApprovedMatchParticipant(item));
            var targetParticipantIsApproved = payment.Booking.Match.MatchParticipants
                .Any(item => item.PlayerId == targetPayerId.Value && IsApprovedMatchParticipant(item));
            if (!currentParticipantIsApproved || !targetParticipantIsApproved)
                return Forbid();
        }
        else if (request.PayerId.HasValue && request.PayerId != currentPlayerId)
        {
            return Forbid();
        }
        if (payment.Booking.Status != "Holding") return Conflict(new { message = $"KhÃƒÆ’Ã‚Â´ng thÃƒÂ¡Ã‚Â»Ã†â€™ thanh toÃƒÆ’Ã‚Â¡n booking {payment.Booking.Status}." });
        if (payment.Booking.HoldExpiresAt <= DateTime.UtcNow)
        {
            await LoadBookingExpiryGraphAsync(payment.Booking, cancellationToken);
            Expire(payment, userId.Value, "HÃƒÂ¡Ã‚ÂºÃ‚Â¿t thÃƒÂ¡Ã‚Â»Ã‚Âi gian giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€ trÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºc khi gÃƒÂ¡Ã‚Â»Ã‚Â­i biÃƒÆ’Ã‚Âªn lai");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Conflict(new { message = "ThÃƒÂ¡Ã‚Â»Ã‚Âi gian giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€ Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ hÃƒÂ¡Ã‚ÂºÃ‚Â¿t." });
        }
        if (payment.Status == "WaitingForConfirmation") return Ok(MapPayment(payment));
        if (payment.Status != "Pending") return Conflict(new { message = $"Thanh toÃƒÆ’Ã‚Â¡n Ãƒâ€žÃ¢â‚¬Ëœang ÃƒÂ¡Ã‚Â»Ã…Â¸ trÃƒÂ¡Ã‚ÂºÃ‚Â¡ng thÃƒÆ’Ã‚Â¡i {payment.Status}." });
        if (string.IsNullOrWhiteSpace(payment.QrImageUrl)) return Conflict(new { message = "ChÃƒÂ¡Ã‚Â»Ã‚Â§ sÃƒÆ’Ã‚Â¢n chÃƒâ€ Ã‚Â°a cÃƒÂ¡Ã‚ÂºÃ‚Â¥u hÃƒÆ’Ã‚Â¬nh tÃƒÆ’Ã‚Â i khoÃƒÂ¡Ã‚ÂºÃ‚Â£n nhÃƒÂ¡Ã‚ÂºÃ‚Â­n tiÃƒÂ¡Ã‚Â»Ã‚Ân." });

        var receiptUrl = await SaveReceiptAsync(payment.PaymentId, receipt, cancellationToken);
        var previous = payment.Status;
        payment.ReceiptImageUrl = receiptUrl;
        payment.Status = "WaitingForConfirmation";
        payment.SubmittedAt = DateTime.UtcNow;
        payment.RejectionReason = null;
        var reviewMinutes = Math.Clamp(
            _configuration.GetValue("Payment:ReviewMinutes", 1440),
            15,
            10080);
        var reviewDeadline = DateTime.UtcNow.AddMinutes(reviewMinutes);
        if (!payment.Booking.HoldExpiresAt.HasValue || payment.Booking.HoldExpiresAt < reviewDeadline)
            payment.Booking.HoldExpiresAt = reviewDeadline;
        payment.StatusHistories.Add(NewHistory(previous, payment.Status, "Submitted", "Player xÃƒÆ’Ã‚Â¡c nhÃƒÂ¡Ã‚ÂºÃ‚Â­n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ chuyÃƒÂ¡Ã‚Â»Ã†â€™n khoÃƒÂ¡Ã‚ÂºÃ‚Â£n", userId));
        _dbContext.VenueAuditLogs.Add(NewAudit(payment.Booking.Court.VenueId, $"PaymentSubmitted:{payment.PaymentId}"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            payment.Booking.Court.VenueId, payment.Booking.CourtId, payment.Booking.StartTime, payment.Booking.EndTime, "Holding", "Updated"));
        PublishPaymentChanged(payment, "Submitted");
        return Ok(MapPayment(payment));
    }
    public async Task<ServiceResult<PaginatedResponse<BankTransferResponse>>> GetOperatorPayments(
        string status = "WaitingForConfirmation",
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var query = AuthorizedOperatorReadQuery(userId.Value);
        if (!status.Equals("All", StringComparison.OrdinalIgnoreCase)) query = query.Where(item => item.Status == status);
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var payments = (await ProjectPaymentResponses(query
            .OrderByDescending(item => item.SubmittedAt)
            .ThenByDescending(item => item.PaymentId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize))
            .ToListAsync(cancellationToken))
            .Select(NormalizePaymentResponseDates)
            .ToList();
        return Ok(Pagination.Create(payments, totalCount, page, pageSize));
    }
    public async Task<ServiceResult<BankTransferResponse>> GetOperatorPayment(int paymentId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var payment = await ProjectPaymentResponses(AuthorizedOperatorReadQuery(userId.Value).Where(item => item.PaymentId == paymentId))
            .SingleOrDefaultAsync(cancellationToken);
        return payment is null ? NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y thanh toÃƒÆ’Ã‚Â¡n trong sÃƒÆ’Ã‚Â¢n Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c phÃƒÆ’Ã‚Â¢n quyÃƒÂ¡Ã‚Â»Ã‚Ân." }) : Ok(NormalizePaymentResponseDates(payment));
    }
    public async Task<ServiceResult<List<BankTransferResponse>>> GetOperatorBookingPayments(
        int bookingId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var payments = (await ProjectPaymentResponses(AuthorizedOperatorReadQuery(userId.Value)
            .Where(item => item.BookingId == bookingId)
            .OrderBy(item => item.Payer.User.Username)
            .ThenBy(item => item.PaymentId))
            .ToListAsync(cancellationToken))
            .Select(NormalizePaymentResponseDates)
            .ToList();
        return payments.Count == 0
            ? NotFound(new { message = "ChÃƒâ€ Ã‚Â°a cÃƒÆ’Ã‚Â³ khoÃƒÂ¡Ã‚ÂºÃ‚Â£n thanh toÃƒÆ’Ã‚Â¡n nÃƒÆ’Ã‚Â o cho nhÃƒÆ’Ã‚Â³m chÃƒâ€ Ã‚Â¡i nÃƒÆ’Ã‚Â y." })
            : Ok(payments);
    }
    public async Task<ServiceResult<BankTransferResponse>> ApprovePayment(int paymentId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"payment-review:{paymentId}", cancellationToken))
            return Conflict(new { message = "Thanh toÃƒÆ’Ã‚Â¡n Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÆ’Ã‚Â½." });
        var payment = await AuthorizedOperatorReviewQuery(userId.Value)
            .SingleOrDefaultAsync(item => item.PaymentId == paymentId, cancellationToken);
        if (payment?.Booking.MatchId is int matchId
            && !await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "TrÃƒÂ¡Ã‚ÂºÃ‚Â­n Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c cÃƒÂ¡Ã‚ÂºÃ‚Â­p nhÃƒÂ¡Ã‚ÂºÃ‚Â­t. Vui lÃƒÆ’Ã‚Â²ng thÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });
        if (payment is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y thanh toÃƒÆ’Ã‚Â¡n trong sÃƒÆ’Ã‚Â¢n Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c phÃƒÆ’Ã‚Â¢n quyÃƒÂ¡Ã‚Â»Ã‚Ân." });

        var groupPayments = payment.PaymentGroupId.HasValue
            ? await AuthorizedOperatorReviewQuery(userId.Value)
                .Where(item => item.BookingId == payment.BookingId
                    && item.PaymentGroupId == payment.PaymentGroupId)
                .OrderBy(item => item.PaymentId)
                .ToListAsync(cancellationToken)
            : [payment];
        if (groupPayments.All(item => item.Status == "Paid")) return Ok(MapPayment(payment));
        if (!groupPayments.All(item => item.Status == "WaitingForConfirmation"))
            return Conflict(new { message = "ToÃƒÆ’Ã‚Â n bÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ giao dÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch phÃƒÂ¡Ã‚ÂºÃ‚Â£i Ãƒâ€žÃ¢â‚¬Ëœang chÃƒÂ¡Ã‚Â»Ã‚Â duyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡t." });
        if (payment.Booking.Status is "Cancelled" or "Expired") return Conflict(new { message = "KhÃƒÆ’Ã‚Â´ng thÃƒÂ¡Ã‚Â»Ã†â€™ xÃƒÆ’Ã‚Â¡c nhÃƒÂ¡Ã‚ÂºÃ‚Â­n booking Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ hÃƒÂ¡Ã‚Â»Ã‚Â§y hoÃƒÂ¡Ã‚ÂºÃ‚Â·c hÃƒÂ¡Ã‚ÂºÃ‚Â¿t hÃƒÂ¡Ã‚ÂºÃ‚Â¡n." });
        if (payment.Booking.Status != "Holding" || payment.Booking.HoldExpiresAt <= DateTime.UtcNow)
        {
            await LoadBookingExpiryGraphAsync(payment.Booking, cancellationToken);
            Expire(payment, userId.Value, "HÃƒÂ¡Ã‚ÂºÃ‚Â¿t thÃƒÂ¡Ã‚Â»Ã‚Âi gian giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€ trÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºc khi chÃƒÂ¡Ã‚Â»Ã‚Â§ sÃƒÆ’Ã‚Â¢n xÃƒÆ’Ã‚Â¡c nhÃƒÂ¡Ã‚ÂºÃ‚Â­n");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Conflict(new { message = "Booking Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ hÃƒÂ¡Ã‚ÂºÃ‚Â¿t thÃƒÂ¡Ã‚Â»Ã‚Âi gian giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€." });
        }

        if (payment.Booking.MatchId.HasValue)
            await LoadMatchPaymentGraphAsync(payment.Booking, cancellationToken);

        var verifiedAt = DateTime.UtcNow;
        foreach (var groupPayment in groupPayments)
        {
            groupPayment.Status = "Paid";
            groupPayment.PaidAt = verifiedAt;
            groupPayment.VerifiedAt = verifiedAt;
            groupPayment.VerifiedByUserId = userId;
            groupPayment.RejectionReason = null;
            groupPayment.StatusHistories.Add(NewHistory(
                "WaitingForConfirmation",
                "Paid",
                groupPayments.Count > 1 ? "BatchApproved" : "Approved",
                groupPayments.Count > 1 ? "Owner/Staff xÃƒÆ’Ã‚Â¡c nhÃƒÂ¡Ã‚ÂºÃ‚Â­n giao dÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch gÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢p" : "Owner/Staff xÃƒÆ’Ã‚Â¡c nhÃƒÂ¡Ã‚ÂºÃ‚Â­n giao dÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch",
                userId));
            _dbContext.VenueAuditLogs.Add(NewAudit(
                groupPayment.Booking.Court.VenueId,
                $"PaymentApproved:{groupPayment.PaymentId}"));
            _notifications.Add(new NotificationInput(
                UserId: groupPayment.Payer.UserId,
                Type: NotificationTypes.Payment,
                Title: "Thanh toÃƒÆ’Ã‚Â¡n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÆ’Ã‚Â¡c nhÃƒÂ¡Ã‚ÂºÃ‚Â­n",
                Message: $"Thanh toÃƒÆ’Ã‚Â¡n cho booking {groupPayment.Booking.BookingCode ?? $"PL-{groupPayment.BookingId}"} Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÆ’Ã‚Â¡c nhÃƒÂ¡Ã‚ÂºÃ‚Â­n.",
                Tone: NotificationTones.Success,
                LinkTo: "/my-bookings",
                LinkLabel: "Xem Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â·t sÃƒÆ’Ã‚Â¢n"));
        }
        FinalizeBookingAfterPaymentApproval(groupPayments[0]);
        if (payment.Booking.Status == "Confirmed") payment.Booking.HoldExpiresAt = null;
        if (payment.Booking.Status == "Confirmed") payment.Booking.StatusHistories.Add(new BookingStatusHistory
        {
            FromStatus = "Holding", ToStatus = "Confirmed", Reason = "Thanh toÃƒÆ’Ã‚Â¡n chuyÃƒÂ¡Ã‚Â»Ã†â€™n khoÃƒÂ¡Ã‚ÂºÃ‚Â£n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÆ’Ã‚Â¡c nhÃƒÂ¡Ã‚ÂºÃ‚Â­n",
            ActorUserId = userId, ChangedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            payment.Booking.Court.VenueId, payment.Booking.CourtId, payment.Booking.StartTime, payment.Booking.EndTime, payment.Booking.Status, "Updated"));
        foreach (var groupPayment in groupPayments)
            PublishPaymentChanged(groupPayment, "Approved");
        return Ok(MapPayment(payment));
    }
    public async Task<ServiceResult<BankTransferResponse>> RejectPayment(
        int paymentId,
        RejectPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"payment-review:{paymentId}", cancellationToken))
            return Conflict(new { message = "Thanh toÃƒÆ’Ã‚Â¡n Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÆ’Ã‚Â½." });
        var payment = await AuthorizedOperatorReviewQuery(userId.Value)
            .SingleOrDefaultAsync(item => item.PaymentId == paymentId, cancellationToken);
        if (payment?.Booking.MatchId is int matchId
            && !await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "TrÃƒÂ¡Ã‚ÂºÃ‚Â­n Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c cÃƒÂ¡Ã‚ÂºÃ‚Â­p nhÃƒÂ¡Ã‚ÂºÃ‚Â­t. Vui lÃƒÆ’Ã‚Â²ng thÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });
        if (payment is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y thanh toÃƒÆ’Ã‚Â¡n trong sÃƒÆ’Ã‚Â¢n Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c phÃƒÆ’Ã‚Â¢n quyÃƒÂ¡Ã‚Â»Ã‚Ân." });

        var groupPayments = payment.PaymentGroupId.HasValue
            ? await AuthorizedOperatorReviewQuery(userId.Value)
                .Where(item => item.BookingId == payment.BookingId
                    && item.PaymentGroupId == payment.PaymentGroupId)
                .OrderBy(item => item.PaymentId)
                .ToListAsync(cancellationToken)
            : [payment];
        if (!groupPayments.All(item => item.Status == "WaitingForConfirmation"))
            return Conflict(new { message = "ToÃƒÆ’Ã‚Â n bÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ giao dÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch phÃƒÂ¡Ã‚ÂºÃ‚Â£i Ãƒâ€žÃ¢â‚¬Ëœang chÃƒÂ¡Ã‚Â»Ã‚Â duyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡t." });
        if (payment.Booking.Status is "Cancelled" or "Expired") return Conflict(new { message = "Booking Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ hÃƒÂ¡Ã‚Â»Ã‚Â§y hoÃƒÂ¡Ã‚ÂºÃ‚Â·c hÃƒÂ¡Ã‚ÂºÃ‚Â¿t hÃƒÂ¡Ã‚ÂºÃ‚Â¡n." });

        var rejectionReason = request.Reason.Trim();
        var verifiedAt = DateTime.UtcNow;
        foreach (var groupPayment in groupPayments)
        {
            groupPayment.Status = "Pending";
            groupPayment.RejectionReason = rejectionReason;
            groupPayment.VerifiedAt = verifiedAt;
            groupPayment.VerifiedByUserId = userId;
            groupPayment.PaymentGroupId = null;
            groupPayment.TransferContent = $"{payment.Booking.BookingCode ?? $"PL-{payment.BookingId}"}-P{groupPayment.PayerId}";
            if (!string.IsNullOrWhiteSpace(groupPayment.BankCode)
                && !string.IsNullOrWhiteSpace(groupPayment.BankAccountNumber)
                && !string.IsNullOrWhiteSpace(groupPayment.BankAccountName))
            {
                groupPayment.QrImageUrl = BuildBatchVietQrUrl(
                    groupPayment.BankCode,
                    groupPayment.BankAccountNumber,
                    groupPayment.BankAccountName,
                    groupPayment.Amount,
                    groupPayment.TransferContent);
            }
            groupPayment.StatusHistories.Add(NewHistory(
                "WaitingForConfirmation",
                "Pending",
                groupPayments.Count > 1 ? "BatchRejected" : "Rejected",
                rejectionReason,
                userId));
            _dbContext.VenueAuditLogs.Add(NewAudit(
                groupPayment.Booking.Court.VenueId,
                $"PaymentRejected:{groupPayment.PaymentId}:{rejectionReason}"));
            _notifications.Add(new NotificationInput(
                UserId: groupPayment.Payer.UserId,
                Type: NotificationTypes.Payment,
                Title: "Thanh toÃƒÆ’Ã‚Â¡n bÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ tÃƒÂ¡Ã‚Â»Ã‚Â« chÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœi",
                Message: $"Thanh toÃƒÆ’Ã‚Â¡n cho booking {groupPayment.Booking.BookingCode ?? $"PL-{groupPayment.BookingId}"} bÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ tÃƒÂ¡Ã‚Â»Ã‚Â« chÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœi: {rejectionReason}",
                Tone: NotificationTones.Urgent,
                LinkTo: "/my-bookings",
                LinkLabel: "GÃƒÂ¡Ã‚Â»Ã‚Â­i lÃƒÂ¡Ã‚ÂºÃ‚Â¡i biÃƒÆ’Ã‚Âªn lai"));
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        foreach (var groupPayment in groupPayments)
            PublishPaymentChanged(groupPayment, "Rejected");
        return Ok(MapPayment(payment));
    }

    private IQueryable<Payment> PaymentSubmissionQuery() => _dbContext.Payments
        .Include(item => item.StatusHistories)
        .Include(item => item.Payer).ThenInclude(item => item.User)
        .Include(item => item.Booking).ThenInclude(item => item.Match).ThenInclude(item => item!.MatchParticipants)
        .Include(item => item.Booking).ThenInclude(item => item.Court).ThenInclude(item => item.Venue);

    private IQueryable<Booking> BatchPaymentBookingQuery(bool asTracking)
    {
        IQueryable<Booking> query = _dbContext.Bookings
            .AsSplitQuery()
            .Include(item => item.Match).ThenInclude(item => item!.MatchParticipants)
            .Include(item => item.Payments).ThenInclude(item => item.StatusHistories)
            .Include(item => item.Payments).ThenInclude(item => item.Payer).ThenInclude(item => item.User)
            .Include(item => item.Court).ThenInclude(item => item.Venue);
        return asTracking ? query : query.AsNoTracking();
    }

    private static bool IsApprovedMatchParticipant(MatchParticipant participant) =>
        participant.Status is "Approved" or "Accepted";

    private static bool HasOneConfiguredBankAccount(IReadOnlyCollection<Payment> payments)
    {
        if (payments.Count == 0 || payments.Any(item =>
                string.IsNullOrWhiteSpace(item.BankCode)
                || string.IsNullOrWhiteSpace(item.BankAccountNumber)
                || string.IsNullOrWhiteSpace(item.BankAccountName)))
            return false;

        return payments
            .Select(item => new { item.BankCode, item.BankAccountNumber, item.BankAccountName })
            .Distinct()
            .Count() == 1;
    }

    private static string BuildBatchTransferContent(Booking booking, IEnumerable<int> payerIds)
    {
        var selection = string.Join(",", payerIds.Order());
        var selectionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selection)))[..12];
        return $"{booking.BookingCode ?? $"PL-{booking.BookingId}"}-G-{selectionHash}";
    }

    private static string BuildBatchVietQrUrl(
        string bankCode,
        string accountNumber,
        string accountName,
        double amount,
        string content)
    {
        var query = $"amount={Math.Round(amount):0}&addInfo={Uri.EscapeDataString(content)}&accountName={Uri.EscapeDataString(accountName)}";
        return $"https://img.vietqr.io/image/{Uri.EscapeDataString(bankCode)}-{Uri.EscapeDataString(accountNumber)}-compact2.png?{query}";
    }

    private static void FinalizeBookingAfterPaymentApproval(Payment payment)
    {
        var match = payment.Booking.Match;
        if (match is null)
        {
            payment.Booking.Status = "Confirmed";
            return;
        }

        var acceptedPlayerIds = match.MatchParticipants
            .Where(item => item.Status == "Approved" || item.Status == "Accepted")
            .Select(item => item.PlayerId)
            .ToHashSet();
        var paidPlayerIds = payment.Booking.Payments
            .Where(item => item.Status == "Paid")
            .Select(item => item.PayerId)
            .ToHashSet();
        var canConfirm = acceptedPlayerIds.Count == match.RequiredPlayerCount
            && acceptedPlayerIds.All(paidPlayerIds.Contains);

        match.Status = canConfirm ? "Booked" : "BookingPending";
        payment.Booking.Status = canConfirm ? "Confirmed" : "Holding";
    }

    private IQueryable<Payment> AuthorizedOperatorReviewQuery(int userId) => PaymentSubmissionQuery()
        .Where(item => item.Booking.Court.Venue.Owner.UserId == userId || item.Booking.Court.Venue.Staff.Any(staff =>
            staff.UserId == userId && staff.IsActive && staff.Permissions.Contains("ConfirmPayment")));

    private async Task LoadBookingExpiryGraphAsync(Booking booking, CancellationToken cancellationToken)
    {
        await _dbContext.Entry(booking).Collection(item => item.Payments).Query()
            .Include(item => item.StatusHistories)
            .LoadAsync(cancellationToken);
        await _dbContext.Entry(booking).Collection(item => item.StatusHistories).LoadAsync(cancellationToken);
        if (booking.MatchId.HasValue)
            await _dbContext.Entry(booking).Reference(item => item.Match).LoadAsync(cancellationToken);
    }

    private async Task LoadMatchPaymentGraphAsync(Booking booking, CancellationToken cancellationToken)
    {
        await _dbContext.Entry(booking).Collection(item => item.Payments).LoadAsync(cancellationToken);
        await _dbContext.Entry(booking).Reference(item => item.Match).Query()
            .Include(item => item!.MatchParticipants)
            .LoadAsync(cancellationToken);
    }

    private IQueryable<Payment> AuthorizedOperatorReadQuery(int userId) => _dbContext.Payments
        .AsNoTracking()
        .AsSplitQuery()
        .Where(item => item.Booking.Court.Venue.Owner.UserId == userId || item.Booking.Court.Venue.Staff.Any(staff =>
            staff.UserId == userId && staff.IsActive && staff.Permissions.Contains("ConfirmPayment")));

    private static IQueryable<BankTransferResponse> ProjectPaymentResponses(IQueryable<Payment> query) =>
        query.Select(payment => new BankTransferResponse
        {
            PaymentId = payment.PaymentId,
            PaymentGroupId = payment.PaymentGroupId,
            GroupPaymentCount = payment.PaymentGroupId.HasValue
                ? payment.Booking.Payments.Count(item => item.PaymentGroupId == payment.PaymentGroupId)
                : 1,
            GroupTotalAmount = payment.PaymentGroupId.HasValue
                ? payment.Booking.Payments
                    .Where(item => item.PaymentGroupId == payment.PaymentGroupId)
                    .Sum(item => item.Amount)
                : payment.Amount,
            BookingId = payment.BookingId,
            BookingCode = payment.Booking.BookingCode ?? string.Empty,
            BookingStatus = payment.Booking.Status,
            PaymentStatus = payment.Status,
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
            HoldExpiresAt = payment.Booking.HoldExpiresAt,
            VenueId = payment.Booking.Court.VenueId,
            VenueName = payment.Booking.Court.Venue.VenueName,
            CourtNumber = payment.Booking.Court.CourtNumber,
            StartTime = payment.Booking.StartTime,
            EndTime = payment.Booking.EndTime,
            PlayerName = payment.Payer.User.Username,
            History = payment.StatusHistories.OrderBy(item => item.CreatedAt).Select(item => new PaymentHistoryResponse
            {
                FromStatus = item.FromStatus,
                ToStatus = item.ToStatus,
                Action = item.Action,
                Reason = item.Reason,
                CreatedAt = item.CreatedAt
            }).ToList()
        });

    private static BankTransferResponse NormalizePaymentResponseDates(BankTransferResponse response)
    {
        response.SubmittedAt = AsUtc(response.SubmittedAt);
        response.VerifiedAt = AsUtc(response.VerifiedAt);
        response.HoldExpiresAt = AsUtc(response.HoldExpiresAt);
        if (string.IsNullOrWhiteSpace(response.BookingCode))
            response.BookingCode = $"PL-{response.BookingId}";
        foreach (var history in response.History)
            history.CreatedAt = AsUtc(history.CreatedAt);
        return response;
    }

    private async Task<VenueOwner?> CurrentOwnerAsync(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        return userId is null ? null : await _dbContext.VenueOwners.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
    }

    private async Task<string> SaveReceiptAsync(int paymentId, IFormFile receipt, CancellationToken cancellationToken)
    {
        var extension = receipt.ContentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var fileName = $"payment-{paymentId}-{Guid.NewGuid():N}{extension}";
        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var directory = Path.Combine(root, "uploads", "payment-receipts");
        Directory.CreateDirectory(directory);
        await using var stream = System.IO.File.Create(Path.Combine(directory, fileName));
        await receipt.CopyToAsync(stream, cancellationToken);
        return PaymentReceiptUrl(fileName);
    }

    private async Task<string> SaveBatchReceiptAsync(Guid paymentGroupId, IFormFile receipt, CancellationToken cancellationToken)
    {
        var extension = receipt.ContentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var fileName = $"payment-group-{paymentGroupId:N}-{Guid.NewGuid():N}{extension}";
        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var directory = Path.Combine(root, "uploads", "payment-receipts");
        Directory.CreateDirectory(directory);
        await using var stream = System.IO.File.Create(Path.Combine(directory, fileName));
        await receipt.CopyToAsync(stream, cancellationToken);
        return PaymentReceiptUrl(fileName);
    }

    private string PaymentReceiptUrl(string fileName)
    {
        var relativeUrl = $"/uploads/payment-receipts/{fileName}";
        var publicBaseUrl = _configuration["PublicBaseUrl"]?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(publicBaseUrl) ? relativeUrl : $"{publicBaseUrl}{relativeUrl}";
    }
    private void Expire(Payment payment, int actorUserId, string reason)
    {
        foreach (var item in payment.Booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation"))
        {
            var previousPayment = item.Status;
            item.Status = "Expired";
            item.StatusHistories.Add(NewHistory(previousPayment, "Expired", "BookingExpired", reason, actorUserId));
        }
        var previousBooking = payment.Booking.Status;
        payment.Booking.Status = "Expired";
        payment.Booking.HoldExpiresAt = null;
        if (payment.Booking.Match is not null)
        {
            var match = payment.Booking.Match;
            var canRetry = !match.AvailableDateTo.HasValue
                || match.AvailableDateTo.Value >= DateOnly.FromDateTime(DateTime.Today);
            match.Status = canRetry ? "ReadyToBook" : "Expired";
            match.CancelledAt = null;
        }
        payment.Booking.StatusHistories.Add(new BookingStatusHistory
        {
            FromStatus = previousBooking, ToStatus = "Expired", Reason = reason, ActorUserId = actorUserId, ChangedAt = DateTime.UtcNow
        });
    }

    private VenueAuditLog NewAudit(int venueId, string action) => new()
    {
        VenueId = venueId, ActorId = CurrentUserId()!.Value, Action = action, Timestamp = DateTime.UtcNow
    };

    private void PublishPaymentChanged(Payment payment, string action)
    {
        _paymentRealtime.Publish(new PaymentChangedEvent(
            payment.PaymentId,
            payment.BookingId,
            payment.Booking.Court.VenueId,
            payment.Status,
            action));
        if (payment.Booking.MatchId.HasValue)
            _matchRealtime.Publish(payment.Booking.MatchId.Value, $"Payment{action}");
    }

    private static PaymentStatusHistory NewHistory(string? from, string to, string action, string? reason, int? actorUserId) => new()
    {
        FromStatus = from, ToStatus = to, Action = action, Reason = reason, ActorUserId = actorUserId, CreatedAt = DateTime.UtcNow
    };

    private static OwnerBankAccountResponse MapAccount(OwnerBankAccount item) => new()
    {
        OwnerBankAccountId = item.OwnerBankAccountId, BankCode = item.BankCode, BankName = item.BankName,
        AccountNumber = item.AccountNumber, AccountHolderName = item.AccountHolderName, IsActive = item.IsActive
    };

    private static BankTransferResponse MapPayment(Payment payment) => new()
    {
        PaymentId = payment.PaymentId,
        PaymentGroupId = payment.PaymentGroupId,
        GroupPaymentCount = payment.PaymentGroupId.HasValue
            ? payment.Booking.Payments.Count(item => item.PaymentGroupId == payment.PaymentGroupId)
            : 1,
        GroupTotalAmount = payment.PaymentGroupId.HasValue
            ? payment.Booking.Payments
                .Where(item => item.PaymentGroupId == payment.PaymentGroupId)
                .Sum(item => item.Amount)
            : payment.Amount,
        BookingId = payment.BookingId,
        BookingCode = payment.Booking.BookingCode ?? $"PL-{payment.BookingId}",
        BookingStatus = payment.Booking.Status,
        PaymentStatus = payment.Status,
        Amount = payment.Amount,
        TransferCode = payment.TransferCode,
        TransferContent = payment.TransferContent,
        BankCode = payment.BankCode,
        BankName = payment.BankName,
        BankAccountNumber = payment.BankAccountNumber,
        BankAccountName = payment.BankAccountName,
        QrImageUrl = payment.QrImageUrl,
        ReceiptImageUrl = payment.ReceiptImageUrl,
        SubmittedAt = AsUtc(payment.SubmittedAt),
        VerifiedAt = AsUtc(payment.VerifiedAt),
        RejectionReason = payment.RejectionReason,
        HoldExpiresAt = AsUtc(payment.Booking.HoldExpiresAt),
        VenueId = payment.Booking.Court.VenueId,
        VenueName = payment.Booking.Court.Venue.VenueName,
        CourtNumber = payment.Booking.Court.CourtNumber,
        StartTime = payment.Booking.StartTime,
        EndTime = payment.Booking.EndTime,
        PlayerName = payment.Payer.User.Username,
        History = payment.StatusHistories.OrderBy(item => item.CreatedAt).Select(item => new PaymentHistoryResponse
        {
            FromStatus = item.FromStatus, ToStatus = item.ToStatus, Action = item.Action,
            Reason = item.Reason, CreatedAt = AsUtc(item.CreatedAt)
        }).ToList()
    };

    public void SetCurrentUserId(int? userId) => _currentUserId = userId;

    private int? _currentUserId;

    private int? CurrentUserId() => _currentUserId;
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
